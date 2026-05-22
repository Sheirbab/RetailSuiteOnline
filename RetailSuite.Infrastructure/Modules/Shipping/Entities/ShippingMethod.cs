using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Shipping.Entities;

/// <summary>
/// A delivery option offered to customers at checkout. Per-tenant configurable.
/// Pricing rule: flat <see cref="BaseFee"/>; if <see cref="FreeOverAmount"/> is set
/// and the cart subtotal meets that threshold, shipping is free.
/// <see cref="IsPickup"/> implies zero fee and no address required.
/// </summary>
public class ShippingMethod : TenantEntity
{
    /// <summary>Stable code — "FLAT", "FREE_OVER_3K", "PICKUP". Used by checkout to record which method was selected.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Display name — "Standard delivery", "Pick up at store".</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Optional explanatory line shown beneath the option.</summary>
    public string? Description { get; private set; }

    /// <summary>Flat rate in rupees applied when threshold not met.</summary>
    public decimal BaseFee { get; private set; }

    /// <summary>If set, orders at or above this subtotal ship free.</summary>
    public decimal? FreeOverAmount { get; private set; }

    /// <summary>True for in-store pickup — bypasses shipping address requirement.</summary>
    public bool IsPickup { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>Display ordering — lower first.</summary>
    public int SortOrder { get; private set; }

    /// <summary>Estimated delivery window shown to customers — "2–4 days", "Same day".</summary>
    public string? Eta { get; private set; }

    private ShippingMethod() { }

    public ShippingMethod(Guid tenantId, string code, string name, decimal baseFee, bool isPickup = false)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Code and Name are required.");

        Id        = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        TenantId  = tenantId;
        Code      = code.Trim().ToUpperInvariant();
        Name      = name.Trim();
        BaseFee   = isPickup ? 0m : Math.Max(0, baseFee);
        IsPickup  = isPickup;
    }

    /// <summary>Compute the fee for a given cart subtotal — honours the free-over threshold.</summary>
    public decimal FeeFor(decimal subtotal)
    {
        if (IsPickup) return 0m;
        if (FreeOverAmount.HasValue && subtotal >= FreeOverAmount.Value) return 0m;
        return BaseFee;
    }

    public void Update(string? name, string? description, decimal? baseFee, decimal? freeOverAmount,
                      bool? isActive, int? sortOrder, string? eta)
    {
        if (!string.IsNullOrWhiteSpace(name))        Name = name.Trim();
        if (description != null)                     Description = description;
        if (baseFee.HasValue)                        BaseFee = IsPickup ? 0m : Math.Max(0, baseFee.Value);
        if (freeOverAmount.HasValue)                 FreeOverAmount = freeOverAmount.Value <= 0 ? null : freeOverAmount.Value;
        if (isActive.HasValue)                       IsActive = isActive.Value;
        if (sortOrder.HasValue)                      SortOrder = sortOrder.Value;
        if (eta != null)                             Eta = eta;
    }
}
