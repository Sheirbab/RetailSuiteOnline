using Microsoft.Extensions.Logging;
using Stripe;

namespace RetailSuite.Infrastructure.Payments;

/// <summary>
/// Handles Stripe webhook events for payment processing.
/// Processes charge.succeeded, charge.failed, charge.refunded events.
/// </summary>
public interface IStripeWebhookHandler
{
    /// <summary>Handle a Stripe webhook event based on type.</summary>
    Task HandleEventAsync(Event stripeEvent);
}

/// <summary>
/// Implementation of Stripe webhook event handler.
/// </summary>
public class StripeWebhookHandler : IStripeWebhookHandler
{
    private readonly ILogger<StripeWebhookHandler> _logger;

    public StripeWebhookHandler(ILogger<StripeWebhookHandler> logger)
    {
        _logger = logger;
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
        // TODO: Send payment confirmation email
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
        // TODO: Send payment failure notification email
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
        // TODO: Send refund confirmation email
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
        // TODO: Update dispute status in database
        await Task.CompletedTask;
    }
}
