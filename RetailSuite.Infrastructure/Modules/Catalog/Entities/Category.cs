using RetailSuite.Shared;

namespace RetailSuite.Modules.Catalog.Entities;

/// <summary>
/// A category that products can be tagged with. Categories are hierarchical via
/// <see cref="ParentCategoryId"/> — a null parent means top-level. The storefront
/// uses the tree for nested filtering and breadcrumbs.
/// </summary>
public class Category : TenantEntity
{
    public string  Name             { get; private set; } = string.Empty;
    public string  Slug             { get; private set; } = string.Empty;
    public Guid?   ParentCategoryId { get; private set; }

    /// <summary>Lower number = displayed first within its parent. Defaults to 0.</summary>
    public int     SortOrder        { get; private set; }

    /// <summary>Soft hide — does not remove the row, just stops it from showing.</summary>
    public bool    IsActive         { get; private set; } = true;

    private Category() { }

    public Category(string name, string slug, Guid? parentId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required.");
        if (string.IsNullOrWhiteSpace(slug)) slug = Product.Slugify(name);

        Id   = Guid.NewGuid();
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        ParentCategoryId = parentId;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required.");
        Name = name.Trim();
    }

    public void SetSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Slug required.");
        Slug = slug.Trim().ToLowerInvariant();
    }

    /// <summary>Move under a new parent. Pass null to make this a top-level category.</summary>
    public void SetParent(Guid? parentId) => ParentCategoryId = parentId;

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void Activate()   => IsActive = true;
    public void Deactivate() => IsActive = false;
}
