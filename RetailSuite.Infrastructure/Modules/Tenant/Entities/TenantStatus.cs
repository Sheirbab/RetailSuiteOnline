namespace RetailSuite.Infrastructure.Modules.Tenant.Entities;

/// <summary>
/// Canonical Tenant lifecycle states.
/// Stored as a plain string in the DB so existing data ("Active") stays valid
/// and ops can read it without enum-int mapping headaches.
/// </summary>
public static class TenantStatus
{
    /// <summary>Newly signed up; email not yet verified.</summary>
    public const string PendingVerification = "PendingVerification";

    /// <summary>Verified, currently in a free trial window.</summary>
    public const string Trialing = "Trialing";

    /// <summary>Verified and on an active (paid or free) plan.</summary>
    public const string Active = "Active";

    /// <summary>Invoice overdue; access still allowed during grace period.</summary>
    public const string PastDue = "PastDue";

    /// <summary>Access blocked due to non-payment or admin action.</summary>
    public const string Suspended = "Suspended";

    /// <summary>Tenant cancelled; data retained read-only for audit.</summary>
    public const string Cancelled = "Cancelled";

    /// <summary>Legacy "Inactive" value kept so old rows still match.</summary>
    public const string Inactive = "Inactive";

    /// <summary>True if a tenant in this status may access tenant APIs.</summary>
    public static bool AllowsAccess(string status) =>
        status == Trialing || status == Active || status == PastDue;
}
