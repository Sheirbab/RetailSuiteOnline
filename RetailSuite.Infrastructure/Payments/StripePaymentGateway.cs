using Microsoft.Extensions.Logging;
using Stripe;

namespace RetailSuite.Infrastructure.Payments;

/// <summary>
/// Production payment gateway using Stripe API.
/// Handles payment processing, refunds, and error handling.
/// </summary>
public class StripePaymentGateway : IPaymentGateway
{
    private readonly ILogger<StripePaymentGateway> _logger;

    public StripePaymentGateway(ILogger<StripePaymentGateway> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Process a payment charge via Stripe.
    /// Requires Stripe API key to be set in environment or StripeConfiguration.
    /// </summary>
    public async Task<PaymentResult> ChargeAsync(decimal amount, string currency, string reference)
    {
        try
        {
            _logger.LogInformation(
                "Processing Stripe charge: Amount={Amount} {Currency}, Reference={Reference}",
                amount, currency, reference);

            // Create charge options
            var chargeOptions = new ChargeCreateOptions
            {
                Amount = (long)(amount * 100), // Stripe uses cents
                Currency = currency.ToLower(),
                Description = $"Order: {reference}",
                StatementDescriptor = "RetailSuite"
            };

            // Process charge
            var chargeService = new ChargeService();
            var charge = await chargeService.CreateAsync(chargeOptions);

            if (charge.Status == "succeeded")
            {
                _logger.LogInformation(
                    "Stripe charge succeeded: ChargeId={ChargeId}, Amount={Amount} {Currency}",
                    charge.Id, charge.Amount / 100m, charge.Currency.ToUpper());

                return new PaymentResult(true, charge.Id, null);
            }
            else
            {
                var error = $"Stripe charge failed with status: {charge.Status}";
                _logger.LogWarning(
                    "Stripe charge failed: ChargeId={ChargeId}, Status={Status}, Reference={Reference}",
                    charge.Id, charge.Status, reference);

                return new PaymentResult(false, charge.Id, error);
            }
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex,
                "Stripe API error: Code={Code}, Message={Message}, Reference={Reference}",
                ex.StripeError?.Code, ex.StripeError?.Message, reference);

            return new PaymentResult(
                false,
                null,
                $"Stripe error: {ex.StripeError?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error processing Stripe charge: Reference={Reference}",
                reference);

            return new PaymentResult(
                false,
                null,
                $"Payment processing error: {ex.Message}");
        }
    }

    /// <summary>
    /// Refund a previous Stripe charge fully or partially.
    /// </summary>
    public async Task<PaymentResult> RefundAsync(string transactionId, decimal amount)
    {
        try
        {
            _logger.LogInformation(
                "Processing Stripe refund: ChargeId={ChargeId}, Amount={Amount}",
                transactionId, amount);

            // Create refund options
            var refundOptions = new RefundCreateOptions
            {
                Charge = transactionId,
                Amount = amount > 0 ? (long)(amount * 100) : null // Null = full refund
            };

            // Process refund
            var refundService = new RefundService();
            var refund = await refundService.CreateAsync(refundOptions);

            if (refund.Status == "succeeded")
            {
                _logger.LogInformation(
                    "Stripe refund succeeded: RefundId={RefundId}, ChargeId={ChargeId}, Amount={Amount}",
                    refund.Id, refund.ChargeId, refund.Amount / 100m);

                return new PaymentResult(true, refund.Id, null);
            }
            else
            {
                var error = $"Stripe refund failed with status: {refund.Status}";
                _logger.LogWarning(
                    "Stripe refund failed: RefundId={RefundId}, Status={Status}",
                    refund.Id, refund.Status);

                return new PaymentResult(false, refund.Id, error);
            }
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex,
                "Stripe refund API error: Code={Code}, Message={Message}, ChargeId={ChargeId}",
                ex.StripeError?.Code, ex.StripeError?.Message, transactionId);

            return new PaymentResult(
                false,
                null,
                $"Stripe refund error: {ex.StripeError?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error processing Stripe refund: ChargeId={ChargeId}",
                transactionId);

            return new PaymentResult(
                false,
                null,
                $"Refund processing error: {ex.Message}");
        }
    }
}
