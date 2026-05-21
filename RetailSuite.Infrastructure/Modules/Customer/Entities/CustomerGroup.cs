namespace RetailSuite.Infrastructure.Modules.Customer.Entities;

/// <summary>
/// Coarse-grained customer segmentation. Useful for reporting (top VIPs, walk-in conversion)
/// and for any future tier-based pricing or loyalty multipliers.
/// </summary>
public enum CustomerGroup
{
    /// <summary>Default — registered customer with normal terms.</summary>
    Regular  = 0,

    /// <summary>VIP — recognised by the retailer; staff may grant courtesies.</summary>
    Vip      = 1,

    /// <summary>Business-to-business — purchases for resale or company use.</summary>
    B2B      = 2,

    /// <summary>Walk-in / anonymous — no contact captured at sale time.</summary>
    Walkin   = 3
}
