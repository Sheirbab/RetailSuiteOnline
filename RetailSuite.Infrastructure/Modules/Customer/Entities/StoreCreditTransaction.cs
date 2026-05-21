using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Customer.Entities;

/// <summary>
/// One ledger entry against a customer's store-credit balance.
/// Positive amount = credit issued (e.g. refund-as-credit, goodwill gesture).
/// Negative amount = credit redeemed (used on a sale).
/// Balance is computed by summing the ledger — never store a denormalised balance,
/// it drifts.
/// </summary>
public class StoreCreditTransaction : TenantEntity
{
    public Guid CustomerId { get; private set; }

    /// <summary>Positive = issued, negative = redeemed.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "PKR";

    public StoreCreditReason Reason { get; private set; }

    /// <summary>Free-text — admin's note when issuing, or OrderNumber when redeemed.</summary>
    public string? Note { get; private set; }

    /// <summary>Order this entry is linked to, if any.</summary>
    public Guid? OrderId { get; private set; }

    /// <summary>User who created the entry (admin/staff who issued, or cashier who redeemed).</summary>
    public Guid? CreatedByUserId { get; private set; }

    private StoreCreditTransaction() { }

    public StoreCreditTransaction(
        Guid tenantId,
        Guid customerId,
        decimal amount,
        StoreCreditReason reason,
        string? note,
        Guid? orderId,
        Guid? createdByUserId,
        string currency = "PKR")
    {
        if (amount == 0)
            throw new ArgumentException("Amount cannot be zero.", nameof(amount));

        Id              = Guid.NewGuid();
        CreatedAt       = DateTime.UtcNow;
        TenantId        = tenantId;
        CustomerId      = customerId;
        Amount          = amount;
        Currency        = string.IsNullOrWhiteSpace(currency) ? "PKR" : currency.ToUpperInvariant();
        Reason          = reason;
        Note            = note;
        OrderId         = orderId;
        CreatedByUserId = createdByUserId;
    }
}

public enum StoreCreditReason
{
    /// <summary>Admin-issued goodwill / promotional credit.</summary>
    Goodwill             = 0,

    /// <summary>Refund settled as store credit instead of cash back.</summary>
    RefundAsCredit       = 1,

    /// <summary>Customer redeemed credit against a sale.</summary>
    RedeemedAgainstSale  = 2,

    /// <summary>Manual adjustment by SuperAdmin (corrections).</summary>
    Adjustment           = 3,

    /// <summary>Credit voided / expired (rare).</summary>
    Voided               = 4
}
