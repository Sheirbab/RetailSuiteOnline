namespace RetailSuite.Infrastructure.Payments;

/// <summary>Abstraction for payment processing. Swap out FakePaymentGateway for a real provider (Stripe, etc.) in production.</summary>
public interface IPaymentGateway
{
    /// <summary>Charge the given amount. Returns a result with a transaction reference on success.</summary>
    Task<PaymentResult> ChargeAsync(decimal amount, string currency, string reference);

    /// <summary>Refund a previous transaction fully or partially.</summary>
    Task<PaymentResult> RefundAsync(string transactionId, decimal amount);
}

/// <summary>Result returned by any payment gateway operation.</summary>
public record PaymentResult(bool Success, string TransactionId, string? Error);
