using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Customer.Entities;

/// <summary>
/// One ledger entry against a customer's loyalty-point balance.
/// Positive Points = earned (e.g. on order complete).
/// Negative Points = redeemed at checkout.
/// Balance is computed by summing the ledger.
/// </summary>
public class LoyaltyTransaction : TenantEntity
{
    public Guid CustomerId { get; private set; }

    /// <summary>Positive = earned, negative = redeemed.</summary>
    public int Points { get; private set; }

    public LoyaltyReason Reason { get; private set; }

    /// <summary>Order that triggered the earn / redemption, if any.</summary>
    public Guid? OrderId { get; private set; }

    /// <summary>Snapshot of the rupee value at redemption time (negative entries) — null for earns.</summary>
    public decimal? RupeesValue { get; private set; }

    public string? Note { get; private set; }

    private LoyaltyTransaction() { }

    public LoyaltyTransaction(
        Guid tenantId,
        Guid customerId,
        int points,
        LoyaltyReason reason,
        Guid? orderId,
        decimal? rupeesValue,
        string? note)
    {
        if (points == 0)
            throw new ArgumentException("Points cannot be zero.", nameof(points));

        Id           = Guid.NewGuid();
        CreatedAt    = DateTime.UtcNow;
        TenantId     = tenantId;
        CustomerId   = customerId;
        Points       = points;
        Reason       = reason;
        OrderId      = orderId;
        RupeesValue  = rupeesValue;
        Note         = note;
    }
}

public enum LoyaltyReason
{
    /// <summary>Auto-earn on order completion.</summary>
    EarnedOnOrder    = 0,

    /// <summary>Redeemed against a sale.</summary>
    Redeemed         = 1,

    /// <summary>Admin manual adjustment (corrections / goodwill).</summary>
    Adjustment       = 2,

    /// <summary>Expired (future feature — points expire after N days).</summary>
    Expired          = 3,

    /// <summary>Reversed because the source order was cancelled / returned.</summary>
    ReversedByReturn = 4
}
