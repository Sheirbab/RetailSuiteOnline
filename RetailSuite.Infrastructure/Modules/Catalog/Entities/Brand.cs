using RetailSuite.Shared;

namespace RetailSuite.Modules.Catalog.Entities;

/// <summary>
/// A brand / manufacturer that a product is associated with. First-class entity
/// so the storefront can filter by brand and group products under brand pages.
/// One brand per product (BrandId on Product); a brand belongs to one tenant.
/// </summary>
public class Brand : TenantEntity
{
    public string  Name        { get; private set; } = string.Empty;
    public string  Slug        { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? LogoUrl     { get; private set; }
    public bool    IsActive    { get; private set; } = true;

    private Brand() { }

    public Brand(Guid tenantId, string name, string slug, string? description = null, string? logoUrl = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug is required.", nameof(slug));

        Id          = Guid.NewGuid();
        TenantId    = tenantId;
        Name        = name.Trim();
        Slug        = slug.Trim().ToLowerInvariant();
        Description = description;
        LogoUrl     = logoUrl;
    }

    public void Update(string name, string slug, string? description, string? logoUrl)
    {
        if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim();
        if (!string.IsNullOrWhiteSpace(slug)) Slug = slug.Trim().ToLowerInvariant();
        Description = description;
        LogoUrl     = logoUrl;
    }

    public void Activate()   => IsActive = true;
    public void Deactivate() => IsActive = false;
}
