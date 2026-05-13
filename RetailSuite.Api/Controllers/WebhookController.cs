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
