using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Customer.Entities;

/// <summary>
/// Per-tenant loyalty configuration. Exactly one row per tenant — created by the
/// loyalty service the first time a tenant uses the feature, then editable by admins.
/// </summary>
/// <remarks>
/// Default formula: spend Rs <see cref="RupeesPerPoint"/> → earn 1 point.
/// Customer redeems <see cref="MinRedeemPoints"/>+ points and each point is worth Rs <see cref="PointValueRupees"/>.
/// </remarks>
public class LoyaltySettings : TenantEntity
{
    /// <summary>How many rupees a customer must spend to earn one point. Default 100.</summary>
    public decimal RupeesPerPoint { get; private set; } = 100m;

    /// <summary>Minimum points required before the customer is allowed to redeem at checkout. Default 100.</summary>
    public int MinRedeemPoints { get; private set; } = 100;

    /// <summary>Value (in rupees) of one redeemed point. Default Re 1 per point.</summary>
    public decimal PointValueRupees { get; private set; } = 1m;

    /// <summary>Maximum % of an order that can be paid with points. Default 50 (= half the order). Range 0–100.</summary>
    public decimal MaxRedemptionPercentOfOrder { get; private set; } = 50m;

    public bool IsEnabled { get; private set; } = true;

    private LoyaltySettings() { }

    public LoyaltySettings(Guid tenantId)
    {
        Id        = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        TenantId  = tenantId;
    }

    public void Update(
        decimal? rupeesPerPoint,
        int?     minRedeemPoints,
        decimal? pointValueRupees,
        decimal? maxRedemptionPercentOfOrder,
        bool?    isEnabled)
    {
        if (rupeesPerPoint.HasValue && rupeesPerPoint.Value > 0) RupeesPerPoint = rupeesPerPoint.Value;
        if (minRedeemPoints.HasValue && minRedeemPoints.Value >= 0) MinRedeemPoints = minRedeemPoints.Value;
        if (pointValueRupees.HasValue && pointValueRupees.Value > 0) PointValueRupees = pointValueRupees.Value;
        if (maxRedemptionPercentOfOrder.HasValue
            && maxRedemptionPercentOfOrder.Value >= 0
            && maxRedemptionPercentOfOrder.Value <= 100)
            MaxRedemptionPercentOfOrder = maxRedemptionPercentOfOrder.Value;
        if (isEnabled.HasValue) IsEnabled = isEnabled.Value;
    }
}
