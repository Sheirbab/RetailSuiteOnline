namespace RetailSuite.Infrastructure.Modules.Subscriptions.Entities;

/// <summary>
/// Lifecycle of a tenant's subscription. Drives access decisions and renewal logic.
/// </summary>
public enum SubscriptionStatus
{
    /// <summary>Free trial window — full access, no payment due yet.</summary>
    Trialing       = 0,

    /// <summary>Active paid (or free) plan; current period is valid.</summary>
    Active         = 1,

    /// <summary>Invoice unpaid past its due date. Access continues during grace.</summary>
    PastDue        = 2,

    /// <summary>Tenant cancelled the subscription. Access ends at EndDate.</summary>
    Cancelled      = 3,

    /// <summary>Suspended due to extended non-payment or admin action.</summary>
    GracePeriod    = 4,

    /// <summary>Expired — period ended without renewal; tenant has no active plan.</summary>
    Expired        = 5
}
