using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Payments;
using Stripe;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Webhook endpoint for Stripe payment events.
/// This endpoint receives HTTP POST requests from Stripe whenever payment events occur.
/// It's crucial for keeping order payment status in sync with actual Stripe transactions.
/// </summary>
[ApiController]
[Route("api/webhooks")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("webhook-strict")]
public class WebhookController : ControllerBase
{
    private readonly IStripeWebhookHandler _webhookHandler;
    private readonly StripeOptions _stripeOptions;
    private readonly IEasyPaisaWebhookHandler _epHandler;
    private readonly IJazzCashWebhookHandler _jcHandler;
    private readonly ISubscriptionPaymentReconciler _reconciler;
    private readonly RetailDbContext _db;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IStripeWebhookHandler webhookHandler,
        IOptions<StripeOptions> stripeOptions,
        IEasyPaisaWebhookHandler epHandler,
        IJazzCashWebhookHandler jcHandler,
        ISubscriptionPaymentReconciler reconciler,
        RetailDbContext db,
        ILogger<WebhookController> logger)
    {
        _webhookHandler = webhookHandler;
        _stripeOptions  = stripeOptions.Value;
        _epHandler      = epHandler;
        _jcHandler      = jcHandler;
        _reconciler     = reconciler;
        _db             = db;
        _logger         = logger;
    }

    /// <summary>
    /// Receive and process Stripe webhook events.
    /// 
    /// IMPORTANT: This endpoint must be publicly accessible and registered in Stripe Dashboard.
    /// Configure in Stripe Settings > Webhooks with your public URL.
    /// Example: https://api.retailsuite.com/api/webhooks/stripe
    /// 
    /// Expected events:
    /// - charge.succeeded: Payment successful
    /// - charge.failed: Payment failed
    /// - charge.refunded: Payment refunded
    /// - charge.dispute.created: Chargeback/dispute initiated
    /// </summary>
    [HttpPost("stripe")]
    [AllowAnonymous] // Webhooks must be accessible without auth
    public async Task<IActionResult> HandleStripeWebhook()
    {
        try
        {
            // Read raw request body
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

            _logger.LogInformation("Received Stripe webhook: {WebhookSize} bytes", json.Length);

            // Verify webhook signature to ensure it came from Stripe
            var stripeSignature = Request.Headers["Stripe-Signature"];

            if (string.IsNullOrWhiteSpace(stripeSignature))
            {
                _logger.LogWarning("Stripe webhook received without signature header");
                return BadRequest("Missing signature");
            }

            if (string.IsNullOrWhiteSpace(_stripeOptions.WebhookSecret))
            {
                _logger.LogError("Stripe webhook secret not configured");
                return StatusCode(500, "Webhook secret not configured");
            }

            // Verify the signature
            Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(
                    json,
                    stripeSignature,
                    _stripeOptions.WebhookSecret);

                _logger.LogInformation(
                    "Stripe webhook signature verified: Type={EventType}, Id={EventId}",
                    stripeEvent.Type, stripeEvent.Id);
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex,
                    "Stripe webhook signature verification failed");
                return BadRequest("Invalid signature");
            }

            // Process the webhook event
            await _webhookHandler.HandleEventAsync(stripeEvent);

            return Ok(new { received = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Stripe webhook");
            return StatusCode(500, "Webhook processing failed");
        }
    }

    // -------------------------------------------------------------
    // POST /api/webhooks/easypaisa
    // -------------------------------------------------------------
    /// <summary>
    /// EasyPaisa Merchant API callback. Configure this URL in your merchant onboarding pack
    /// as the postBackURL. Returns 200 OK regardless of outcome (after persisting the event)
    /// so EasyPaisa does not retry indefinitely.
    /// </summary>
    [HttpPost("easypaisa")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleEasyPaisaWebhook()
    {
        var body = await ReadBodyAsync();
        var result = _epHandler.Verify(body);
        return await IngestAsync("EasyPaisa", body, result);
    }

    // -------------------------------------------------------------
    // POST /api/webhooks/jazzcash
    // -------------------------------------------------------------
    /// <summary>
    /// JazzCash callback (ReturnURL / IPN). Configure this URL in your merchant onboarding pack.
    /// </summary>
    [HttpPost("jazzcash")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleJazzCashWebhook()
    {
        var body = await ReadBodyAsync();
        var result = _jcHandler.Verify(body);
        return await IngestAsync("JazzCash", body, result);
    }

    // ============================================================
    //  SuperAdmin ops endpoints — list + replay failed webhooks
    // ============================================================

    /// <summary>
    /// List recent WebhookEvent records. Filter by <c>processed=true|false</c> and / or
    /// <c>provider=Stripe|EasyPaisa|JazzCash</c>. Newest first, capped at 200.
    /// </summary>
    [HttpGet("events")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> ListEvents(
        [FromQuery] bool? processed,
        [FromQuery] string? provider)
    {
        var q = _db.WebhookEvents.AsQueryable();
        if (processed.HasValue) q = q.Where(w => w.Processed == processed.Value);
        if (!string.IsNullOrWhiteSpace(provider)) q = q.Where(w => w.Provider == provider);

        var rows = await q
            .OrderByDescending(w => w.CreatedAt)
            .Take(200)
            .Select(w => new
            {
                w.Id,
                w.Provider,
                w.ExternalEventId,
                w.EventType,
                w.Processed,
                w.ProcessedAt,
                w.ProcessingError,
                w.MatchedSubscriptionPaymentId,
                w.MatchedOrderPaymentId,
                w.CreatedAt
            })
            .ToListAsync();

        return Ok(new { count = rows.Count, events = rows });
    }

    /// <summary>
    /// Re-run reconciliation for a previously-stored WebhookEvent. Useful when a delivery
    /// arrived during a deploy window and silently failed, or when a code fix means an
    /// event that previously errored can now be processed.
    /// </summary>
    /// <remarks>
    /// Parses the raw payload again through the matching provider's handler so the signature
    /// check still runs. If the event is already marked Processed (i.e. it succeeded), this
    /// is a no-op that returns 200 — the reconciler itself is idempotent so even a forced
    /// replay is safe.
    /// </remarks>
    [HttpPost("events/{id:guid}/replay")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> Replay(Guid id)
    {
        var record = await _db.WebhookEvents.FirstOrDefaultAsync(w => w.Id == id);
        if (record == null)
            return NotFound(new { error = "Event not found." });

        // Re-verify the signature so a replay can't bypass auth (raw payload is preserved).
        WebhookHandleResult parsed = record.Provider switch
        {
            "EasyPaisa" => _epHandler.Verify(record.RawPayload),
            "JazzCash"  => _jcHandler.Verify(record.RawPayload),
            _           => new WebhookHandleResult(
                                Accepted:        false,
                                ExternalEventId: null,
                                EventType:       null,
                                ProviderTxnRef:  null,
                                Succeeded:       false,
                                Amount:          0m,
                                FailureReason:   null,
                                RejectReason:    $"Provider '{record.Provider}' does not support replay.")
        };

        if (!parsed.Accepted)
        {
            record.MarkFailed($"Replay rejected: {parsed.RejectReason}");
            await _db.SaveChangesAsync();
            return BadRequest(new { error = parsed.RejectReason });
        }

        try
        {
            var reco = await _reconciler.ReconcileAsync(
                providerTxnRef: parsed.ProviderTxnRef ?? string.Empty,
                succeeded:      parsed.Succeeded,
                amount:         parsed.Amount,
                failureReason:  parsed.FailureReason);

            record.MarkProcessed(subscriptionPaymentId: reco.SubscriptionPaymentId);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "WebhookEvent {Id} replayed by SuperAdmin: reconciled={Reconciled}",
                id, reco.Reconciled);

            return Ok(new
            {
                replayed   = true,
                reconciled = reco.Reconciled,
                paymentId  = reco.SubscriptionPaymentId,
                invoiceId  = reco.InvoiceId,
                note       = reco.Reason
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Replay failed for WebhookEvent {Id}", id);
            record.MarkFailed(ex.Message);
            await _db.SaveChangesAsync();
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // -------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------

    private async Task<string> ReadBodyAsync()
    {
        using var reader = new StreamReader(HttpContext.Request.Body);
        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// Common ingestion pipeline for non-Stripe webhooks:
    ///   1. If signature invalid → 400. Don't persist (don't pollute audit with spam).
    ///   2. Otherwise persist a WebhookEvent (Pending) with idempotency on (Provider,EventId).
    ///   3. If duplicate → return 200 immediately.
    ///   4. Call the reconciler, mark Processed/Failed, return 200.
    /// </summary>
    private async Task<IActionResult> IngestAsync(string provider, string rawBody, WebhookHandleResult result)
    {
        if (!result.Accepted)
        {
            _logger.LogWarning("{Provider} webhook rejected: {Reason}", provider, result.RejectReason);
            return BadRequest(new { reason = result.RejectReason });
        }

        // Idempotency check.
        var existing = await _db.WebhookEvents
            .FirstOrDefaultAsync(w => w.Provider == provider && w.ExternalEventId == result.ExternalEventId);

        if (existing != null)
        {
            _logger.LogInformation(
                "{Provider} duplicate webhook ignored: EventId={EventId}, AlreadyProcessed={Processed}",
                provider, result.ExternalEventId, existing.Processed);
            return Ok(new { received = true, duplicate = true });
        }

        var record = new WebhookEvent(provider, result.ExternalEventId ?? string.Empty,
                                       result.EventType ?? string.Empty, rawBody);
        _db.WebhookEvents.Add(record);
        await _db.SaveChangesAsync();

        try
        {
            var reco = await _reconciler.ReconcileAsync(
                providerTxnRef: result.ProviderTxnRef ?? string.Empty,
                succeeded:      result.Succeeded,
                amount:         result.Amount,
                failureReason:  result.FailureReason);

            record.MarkProcessed(subscriptionPaymentId: reco.SubscriptionPaymentId);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                received      = true,
                reconciled    = reco.Reconciled,
                paymentId     = reco.SubscriptionPaymentId,
                invoiceId     = reco.InvoiceId,
                note          = reco.Reason
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Provider} webhook processing failed.", provider);
            record.MarkFailed(ex.Message);
            await _db.SaveChangesAsync();
            // Still 200 so the provider doesn't retry; we have the event persisted for replay.
            return Ok(new { received = true, processed = false, error = ex.Message });
        }
    }
}
