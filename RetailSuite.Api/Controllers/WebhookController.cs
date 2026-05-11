using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IStripeWebhookHandler webhookHandler,
        IOptions<StripeOptions> stripeOptions,
        ILogger<WebhookController> logger)
    {
        _webhookHandler = webhookHandler;
        _stripeOptions = stripeOptions.Value;
        _logger = logger;
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
}
