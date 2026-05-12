using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure.Email;
using Stripe;

namespace RetailSuite.Infrastructure.Payments;

/// <summary>
/// Handles Stripe webhook events for payment processing.
/// Processes charge.succeeded, charge.failed, charge.refunded events.
/// Sends email notifications for each event.
/// </summary>
public interface IStripeWebhookHandler
{
    /// <summary>Handle a Stripe webhook event based on type.</summary>
    Task HandleEventAsync(Event stripeEvent);
}

/// <summary>
/// Implementation of Stripe webhook event handler.
/// Sends payment confirmation/failure emails on events.
/// </summary>
public class StripeWebhookHandler : IStripeWebhookHandler
{
    private readonly ILogger<StripeWebhookHandler> _logger;
    private readonly IEmailService _emailService;

    public StripeWebhookHandler(
        ILogger<StripeWebhookHandler> logger,
        IEmailService emailService)
    {
        _logger = logger;
        _emailService = emailService;
    }

    /// <summary>
    /// Process Stripe webhook events.
    /// Typical events: charge.succeeded, charge.failed, charge.refunded, charge.dispute.created
    /// </summary>
    public async Task HandleEventAsync(Event stripeEvent)
    {
        _logger.LogInformation(
            "Processing Stripe webhook event: Type={EventType}, Id={EventId}, CreatedAt={CreatedAt}",
            stripeEvent.Type, stripeEvent.Id, stripeEvent.Created);

        try
        {
            switch (stripeEvent.Type)
            {
                case "charge.succeeded":
                    await HandleChargeSucceededAsync(stripeEvent);
                    break;

                case "charge.failed":
                    await HandleChargeFailedAsync(stripeEvent);
                    break;

                case "charge.refunded":
                    await HandleChargeRefundedAsync(stripeEvent);
                    break;

                case "charge.dispute.created":
                    await HandleChargeDisputeCreatedAsync(stripeEvent);
                    break;

                default:
                    _logger.LogInformation(
                        "Unhandled Stripe webhook event type: {EventType}",
                        stripeEvent.Type);
                    break;
            }

            _logger.LogInformation(
                "Successfully processed Stripe webhook event: Type={EventType}, Id={EventId}",
                stripeEvent.Type, stripeEvent.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing Stripe webhook event: Type={EventType}, Id={EventId}",
                stripeEvent.Type, stripeEvent.Id);

            throw; // Re-throw to signal Stripe that webhook wasn't processed
        }
    }

    /// <summary>Handle successful payment charge.</summary>
    private async Task HandleChargeSucceededAsync(Event stripeEvent)
    {
        var charge = stripeEvent.Data.Object as Charge;
        if (charge == null)
        {
            _logger.LogWarning("charge.succeeded event missing charge data");
            return;
        }

        _logger.LogInformation(
            "Charge succeeded webhook: ChargeId={ChargeId}, Amount={Amount} {Currency}, OrderRef={OrderRef}",
            charge.Id,
            charge.Amount / 100m,
            charge.Currency.ToUpper(),
            charge.Description);

        // TODO: Update order payment status in database
        // For now, just log and send email

        // Extract customer email from metadata (if available)
        var customerEmail = charge.Metadata?.ContainsKey("customer_email") == true
            ? charge.Metadata["customer_email"]
            : null;

        if (!string.IsNullOrWhiteSpace(customerEmail))
        {
            var subject = $"Payment Confirmation - Order {charge.Description}";
            var htmlBody = GeneratePaymentConfirmationEmail(
                charge.Description,
                charge.Amount / 100m,
                charge.Currency.ToUpper(),
                charge.Id);

            _logger.LogInformation("Sending payment confirmation email to {Email}", customerEmail);
            await _emailService.SendAsync(customerEmail, subject, htmlBody);
        }
        else
        {
            _logger.LogWarning(
                "No customer email found for charge {ChargeId}. Consider adding to metadata.",
                charge.Id);
        }

        await Task.CompletedTask;
    }

    /// <summary>Handle failed payment charge.</summary>
    private async Task HandleChargeFailedAsync(Event stripeEvent)
    {
        var charge = stripeEvent.Data.Object as Charge;
        if (charge == null)
        {
            _logger.LogWarning("charge.failed event missing charge data");
            return;
        }

        _logger.LogWarning(
            "Charge failed webhook: ChargeId={ChargeId}, Amount={Amount} {Currency}, Reason={Reason}, OrderRef={OrderRef}",
            charge.Id,
            charge.Amount / 100m,
            charge.Currency.ToUpper(),
            charge.FailureMessage,
            charge.Description);

        // TODO: Update order payment status to failed

        // Extract customer email from metadata
        var customerEmail = charge.Metadata?.ContainsKey("customer_email") == true
            ? charge.Metadata["customer_email"]
            : null;

        if (!string.IsNullOrWhiteSpace(customerEmail))
        {
            var subject = $"Payment Failed - Order {charge.Description}";
            var htmlBody = GeneratePaymentFailureEmail(
                charge.Description,
                charge.Amount / 100m,
                charge.Currency.ToUpper(),
                charge.FailureMessage ?? "Payment declined");

            _logger.LogInformation("Sending payment failure email to {Email}", customerEmail);
            await _emailService.SendAsync(customerEmail, subject, htmlBody);
        }

        await Task.CompletedTask;
    }

    /// <summary>Handle charge refund.</summary>
    private async Task HandleChargeRefundedAsync(Event stripeEvent)
    {
        var charge = stripeEvent.Data.Object as Charge;
        if (charge == null)
        {
            _logger.LogWarning("charge.refunded event missing charge data");
            return;
        }

        _logger.LogInformation(
            "Charge refunded webhook: ChargeId={ChargeId}, RefundedAmount={RefundedAmount} {Currency}, OrderRef={OrderRef}",
            charge.Id,
            charge.AmountRefunded / 100m,
            charge.Currency.ToUpper(),
            charge.Description);

        // TODO: Update refund status in database

        // Extract customer email
        var customerEmail = charge.Metadata?.ContainsKey("customer_email") == true
            ? charge.Metadata["customer_email"]
            : null;

        if (!string.IsNullOrWhiteSpace(customerEmail))
        {
            var subject = $"Refund Confirmation - Order {charge.Description}";
            var htmlBody = GenerateRefundConfirmationEmail(
                charge.Description,
                charge.AmountRefunded / 100m,
                charge.Currency.ToUpper(),
                charge.Id);

            _logger.LogInformation("Sending refund confirmation email to {Email}", customerEmail);
            await _emailService.SendAsync(customerEmail, subject, htmlBody);
        }

        await Task.CompletedTask;
    }

    /// <summary>Handle charge dispute creation.</summary>
    private async Task HandleChargeDisputeCreatedAsync(Event stripeEvent)
    {
        var dispute = stripeEvent.Data.Object as Dispute;
        if (dispute == null)
        {
            _logger.LogWarning("charge.dispute.created event missing dispute data");
            return;
        }

        _logger.LogWarning(
            "Charge dispute created webhook: DisputeId={DisputeId}, ChargeId={ChargeId}, Reason={Reason}, Amount={Amount}",
            dispute.Id,
            dispute.ChargeId,
            dispute.Reason,
            dispute.Amount / 100m);

        // TODO: Alert admin of dispute
        // For now, just log
        await Task.CompletedTask;
    }

    /// <summary>Generate HTML email for payment confirmation.</summary>
    private string GeneratePaymentConfirmationEmail(string orderNumber, decimal amount, string currency, string transactionId)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <style>
        body {{ font-family: Arial, sans-serif; background-color: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 20px auto; background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .header {{ background: #4CAF50; color: white; padding: 20px; border-radius: 4px; text-align: center; }}
        .content {{ padding: 20px; }}
        .detail-row {{ display: flex; justify-content: space-between; padding: 10px 0; border-bottom: 1px solid #eee; }}
        .detail-label {{ font-weight: bold; color: #333; }}
        .detail-value {{ color: #666; }}
        .amount {{ font-size: 24px; font-weight: bold; color: #4CAF50; }}
        .footer {{ text-align: center; color: #999; font-size: 12px; margin-top: 20px; padding-top: 20px; border-top: 1px solid #eee; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>✓ Payment Confirmed</h1>
        </div>
        <div class='content'>
            <p>Thank you for your payment! Your order has been successfully processed.</p>

            <div class='detail-row'>
                <span class='detail-label'>Order Number:</span>
                <span class='detail-value'>{orderNumber}</span>
            </div>

            <div class='detail-row'>
                <span class='detail-label'>Amount:</span>
                <span class='amount'>{amount:N2} {currency}</span>
            </div>

            <div class='detail-row'>
                <span class='detail-label'>Transaction ID:</span>
                <span class='detail-value'>{transactionId}</span>
            </div>

            <div class='detail-row'>
                <span class='detail-label'>Date:</span>
                <span class='detail-value'>{DateTime.Now:MMM dd, yyyy HH:mm:ss}</span>
            </div>

            <p style='margin-top: 20px;'>You can track your order status in your account. If you have any questions, please contact our support team.</p>
        </div>
        <div class='footer'>
            <p>RetailSuite - Your Shopping Partner</p>
        </div>
    </div>
</body>
</html>";
    }

    /// <summary>Generate HTML email for payment failure.</summary>
    private string GeneratePaymentFailureEmail(string orderNumber, decimal amount, string currency, string failureReason)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <style>
        body {{ font-family: Arial, sans-serif; background-color: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 20px auto; background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .header {{ background: #f44336; color: white; padding: 20px; border-radius: 4px; text-align: center; }}
        .content {{ padding: 20px; }}
        .detail-row {{ display: flex; justify-content: space-between; padding: 10px 0; border-bottom: 1px solid #eee; }}
        .detail-label {{ font-weight: bold; color: #333; }}
        .detail-value {{ color: #666; }}
        .amount {{ font-size: 24px; font-weight: bold; color: #f44336; }}
        .reason {{ background: #fff3e0; padding: 10px; border-left: 4px solid #ff9800; margin: 15px 0; }}
        .footer {{ text-align: center; color: #999; font-size: 12px; margin-top: 20px; padding-top: 20px; border-top: 1px solid #eee; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>✗ Payment Failed</h1>
        </div>
        <div class='content'>
            <p>Unfortunately, your payment could not be processed.</p>

            <div class='detail-row'>
                <span class='detail-label'>Order Number:</span>
                <span class='detail-value'>{orderNumber}</span>
            </div>

            <div class='detail-row'>
                <span class='detail-label'>Amount:</span>
                <span class='amount'>{amount:N2} {currency}</span>
            </div>

            <div class='reason'>
                <strong>Reason:</strong> {failureReason}
            </div>

            <p>Please try the following:</p>
            <ul>
                <li>Check your card details are correct</li>
                <li>Ensure sufficient funds are available</li>
                <li>Try a different payment method</li>
                <li>Contact your bank if the issue persists</li>
            </ul>

            <p>You can retry your payment in your account. If you need assistance, our support team is here to help.</p>
        </div>
        <div class='footer'>
            <p>RetailSuite - Your Shopping Partner</p>
        </div>
    </div>
</body>
</html>";
    }

    /// <summary>Generate HTML email for refund confirmation.</summary>
    private string GenerateRefundConfirmationEmail(string orderNumber, decimal amount, string currency, string transactionId)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <style>
        body {{ font-family: Arial, sans-serif; background-color: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 20px auto; background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .header {{ background: #2196F3; color: white; padding: 20px; border-radius: 4px; text-align: center; }}
        .content {{ padding: 20px; }}
        .detail-row {{ display: flex; justify-content: space-between; padding: 10px 0; border-bottom: 1px solid #eee; }}
        .detail-label {{ font-weight: bold; color: #333; }}
        .detail-value {{ color: #666; }}
        .amount {{ font-size: 24px; font-weight: bold; color: #2196F3; }}
        .footer {{ text-align: center; color: #999; font-size: 12px; margin-top: 20px; padding-top: 20px; border-top: 1px solid #eee; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Refund Processed</h1>
        </div>
        <div class='content'>
            <p>Your refund has been successfully processed and will appear in your account within 3-5 business days.</p>

            <div class='detail-row'>
                <span class='detail-label'>Order Number:</span>
                <span class='detail-value'>{orderNumber}</span>
            </div>

            <div class='detail-row'>
                <span class='detail-label'>Refund Amount:</span>
                <span class='amount'>{amount:N2} {currency}</span>
            </div>

            <div class='detail-row'>
                <span class='detail-label'>Transaction ID:</span>
                <span class='detail-value'>{transactionId}</span>
            </div>

            <div class='detail-row'>
                <span class='detail-label'>Date:</span>
                <span class='detail-value'>{DateTime.Now:MMM dd, yyyy HH:mm:ss}</span>
            </div>

            <p style='margin-top: 20px;'>If you have any questions about this refund, please contact our support team.</p>
        </div>
        <div class='footer'>
            <p>RetailSuite - Your Shopping Partner</p>
        </div>
    </div>
</body>
</html>";
    }
}
