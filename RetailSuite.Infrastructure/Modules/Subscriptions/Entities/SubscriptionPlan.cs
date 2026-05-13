using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Subscriptions.Entities;

/// <summary>
/// Catalog entry for an offerable subscription plan.
/// Global — NOT tenant-scoped, so the same plan applies to all tenants.
/// SuperAdmin manages this list via /api/subscriptions/plans endpoints.
/// </summary>
public class SubscriptionPlan : BaseEntity
{
    /// <summary>Stable identifier — "FREE", "STARTER", "PRO", "ENTERPRISE", etc. Uppercase, no spaces.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Display name shown to customers.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Short marketing description.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>Monthly price in <see cref="Currency"/>. Zero = free.</summary>
    public decimal MonthlyPrice { get; private set; }

    /// <summary>Yearly price in <see cref="Currency"/>. Should reflect any annual discount.</summary>
    public decimal YearlyPrice { get; private set; }

    /// <summary>ISO 4217 currency code. PKR by default.</summary>
    public string Currency { get; private set; } = "PKR";

    /// <summary>Length of the free trial in days. 0 = no trial.</summary>
    public int TrialDays { get; private set; }

    /// <summary>Whether the plan can be selected by new signups. Discontinued plans are hidden.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Display order on the pricing page. Lower number = shown first.</summary>
    public int SortOrder { get; private set; }

    // ---- Limits (null = unlimited) -------------------------------------
    public int? MaxUsers { get; private set; }
    public int? MaxProducts { get; private set; }
    public int? MaxOrdersPerMonth { get; private set; }
    public int? MaxStorageMb { get; private set; }

    // ---- Feature flags --------------------------------------------------
    public bool ApiAccess { get; private set; }
    public bool MultiStore { get; private set; }
    public bool AdvancedAnalytics { get; private set; }
    public bool WebhooksEnabled { get; private set; }
    public bool PrioritySupport { get; private set; }

    private SubscriptionPlan() { }

    public SubscriptionPlan(
        string code,
        string name,
        string description,
        decimal monthlyPrice,
        decimal yearlyPrice,
        int trialDays = 14,
        string currency = "PKR")
    {
        Id           = Guid.NewGuid();
        CreatedAt    = DateTime.UtcNow;
        Code         = code.Trim().ToUpperInvariant();
        Name         = name;
        Description  = description ?? string.Empty;
        MonthlyPrice = monthlyPrice;
        YearlyPrice  = yearlyPrice;
        TrialDays    = trialDays;
        Currency     = string.IsNullOrWhiteSpace(currency) ? "PKR" : currency.ToUpperInvariant();
    }

    /// <summary>Returns the price for the given billing cycle.</summary>
    public decimal PriceFor(BillingCycle cycle) =>
        cycle == BillingCycle.Yearly ? YearlyPrice : MonthlyPrice;

    /// <summary>True if the plan has any cost (i.e. not the free plan).</summary>
    public bool IsPaid => MonthlyPrice > 0 || YearlyPrice > 0;

    // ---- Mutators (used by SuperAdmin endpoints) ------------------------

    public void UpdatePricing(decimal monthlyPrice, decimal yearlyPrice, string currency)
    {
        MonthlyPrice = monthlyPrice;
        YearlyPrice  = yearlyPrice;
        Currency     = string.IsNullOrWhiteSpace(currency) ? Currency : currency.ToUpperInvariant();
    }

    public void UpdateNameAndDescription(string name, string description)
    {
        Name        = name;
        Description = description ?? string.Empty;
    }

    public void UpdateTrialDays(int days) => TrialDays = Math.Max(0, days);

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void Activate()   => IsActive = true;
    public void Deactivate() => IsActive = false;

    public void UpdateLimits(int? maxUsers, int? maxProducts, int? maxOrdersPerMonth, int? maxStorageMb)
    {
        MaxUsers          = maxUsers;
        MaxProducts       = maxProducts;
        MaxOrdersPerMonth = maxOrdersPerMonth;
        MaxStorageMb      = maxStorageMb;
    }

    public void UpdateFeatures(
        bool apiAccess,
        bool multiStore,
        bool advancedAnalytics,
        bool webhooksEnabled,
        bool prioritySupport)
    {
        ApiAccess          = apiAccess;
        MultiStore         = multiStore;
        AdvancedAnalytics  = advancedAnalytics;
        WebhooksEnabled    = webhooksEnabled;
        PrioritySupport    = prioritySupport;
    }
}
