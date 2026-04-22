namespace RetailSuite.Infrastructure.Payments;

/// <summary>
/// Stub payment gateway for development / demo. Always succeeds.
/// Replace with a real provider (Stripe, PayFast, etc.) before going live.
/// </summary>
public class FakePaymentGateway : IPaymentGateway
{
    public Task<PaymentResult> ChargeAsync(decimal amount, string currency, string reference)
    {
        var txnId = $"TXN-FAKE-{Guid.NewGuid():N}";
        return Task.FromResult(new PaymentResult(true, txnId, null));
    }

    public Task<PaymentResult> RefundAsync(string transactionId, decimal amount)
    {
        var refundId = $"REF-FAKE-{Guid.NewGuid():N}";
        return Task.FromResult(new PaymentResult(true, refundId, null));
    }
}
