using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Identity.Entities;

/// <summary>
/// One row per (user, permission code). The presence of a row grants the permission;
/// removing the row revokes it. Codes are unconstrained strings — see
/// <see cref="Permissions.Catalog"/> for the known set.
/// </summary>
public class UserPermission : TenantEntity
{
    public Guid UserId { get; private set; }

    /// <summary>Permission code (e.g. "POS", "INVENTORY_VIEW"). See <see cref="Permissions"/>.</summary>
    public string Code { get; private set; } = string.Empty;

    private UserPermission() { }

    public UserPermission(Guid tenantId, Guid userId, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required.", nameof(code));

        Id        = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        TenantId  = tenantId;
        UserId    = userId;
        Code      = code.Trim();
    }
}
