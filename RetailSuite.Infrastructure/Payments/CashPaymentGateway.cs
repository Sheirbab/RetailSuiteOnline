namespace RetailSuite.Infrastructure.Payments;

/// <summary>
/// Cash payment gateway — records cash transactions locally.
/// No external API call needed; always succeeds.
/// </summary>
public class CashPaymentGateway : IPaymentGateway
{
    public Task<PaymentResult> ChargeAsync(decimal amount, string currency, string reference)
    {
        var txnId = $"CASH-{Guid.NewGuid():N}";
        return Task.FromResult(new PaymentResult(true, txnId, null));
    }

    public Task<PaymentResult> RefundAsync(string transactionId, decimal amount)
    {
        var refundId = $"CASH-REF-{Guid.NewGuid():N}";
        return Task.FromResult(new PaymentResult(true, refundId, null));
    }
}
