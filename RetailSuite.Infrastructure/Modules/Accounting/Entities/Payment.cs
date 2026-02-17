using RetailSuite.Shared;

namespace RetailSuite.Modules.Accounting.Entities;

public class Payment : TenantEntity
{
    public Guid OrderId { get; private set; }

    public decimal Amount { get; private set; }

    public string PaymentMethod { get; private set; }

    public DateTime PaidAt { get; private set; }
    public string? TransactionReference { get; private set; }

    private Payment() { }

    public Payment(Guid orderId, decimal amount, string method)
    {
        OrderId = orderId;
        Amount = amount;
        PaymentMethod = method;
        PaidAt = DateTime.UtcNow;
    }
}