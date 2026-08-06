using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Tenant.Entities;

/// <summary>
/// Record of a SuperAdmin action taken on a tenant (status change, edit, archive).
/// Platform-level record-keeping, not tenant-owned data — deliberately extends
/// BaseEntity (not TenantEntity) so it's never subject to the tenant query filter.
/// </summary>
public class TenantAuditLog : BaseEntity
{
    public Guid   TenantId          { get; private set; }
    public Guid   PerformedByUserId { get; private set; }
    public string PerformedByEmail  { get; private set; } = string.Empty;

    /// <summary>Short machine-readable action name, e.g. "StatusChanged", "Edited", "Archived".</summary>
    public string Action  { get; private set; } = string.Empty;

    /// <summary>Human-readable description of what changed, e.g. "Status: Active -> Suspended".</summary>
    public string Details { get; private set; } = string.Empty;

    private TenantAuditLog() { }

    public TenantAuditLog(Guid tenantId, Guid performedByUserId, string performedByEmail, string action, string details)
    {
        Id                = Guid.NewGuid();
        CreatedAt         = DateTime.UtcNow;
        TenantId          = tenantId;
        PerformedByUserId = performedByUserId;
        PerformedByEmail  = performedByEmail;
        Action            = action;
        Details           = details;
    }
}
