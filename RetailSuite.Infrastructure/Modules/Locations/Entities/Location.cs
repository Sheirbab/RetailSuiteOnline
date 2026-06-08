using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Locations.Entities;

/// <summary>
/// A physical branch / shop the tenant operates. Every <see cref="Inventory.Entities.InventoryItem"/>
/// row belongs to one Location, and every sale + receipt + transfer is scoped to one.
///
/// Each tenant has exactly one <see cref="IsDefault"/> location — used by the storefront
/// for online orders and by the POS when no explicit location is selected. Auto-seeded as
/// "Main Branch" on tenant creation.
/// </summary>
public class Location : TenantEntity
{
    /// <summary>Short stable code — e.g. "MAIN", "DHA", "KHI-01". Used in reports / barcode prefixes.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Display name shown in the UI — e.g. "Main Branch", "DHA Phase 6".</summary>
    public string Name { get; private set; } = string.Empty;

    public string? Address { get; private set; }

    public string? Phone { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Exactly one location per tenant is marked default. Enforced by the
    /// <see cref="Services.ILocationService"/>.
    /// </summary>
    public bool IsDefault { get; private set; }

    /// <summary>Free-text notes for ops (opening hours, manager name, etc.).</summary>
    public string? Notes { get; private set; }

    private Location() { }

    public Location(Guid tenantId, string code, string name, bool isDefault = false)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        Id        = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        TenantId  = tenantId;
        Code      = code.Trim().ToUpperInvariant();
        Name      = name.Trim();
        IsDefault = isDefault;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        Name = name.Trim();
    }

    public void UpdateContact(string? address, string? phone, string? notes)
    {
        Address = address;
        Phone   = phone;
        Notes   = notes;
    }

    public void Activate()   => IsActive = true;
    public void Deactivate() => IsActive = false;

    /// <summary>Mark this location as the tenant's default. The service is responsible for unsetting the previous default.</summary>
    public void SetDefault(bool isDefault) => IsDefault = isDefault;
}
