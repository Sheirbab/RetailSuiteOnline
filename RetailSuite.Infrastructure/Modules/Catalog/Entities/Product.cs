using RetailSuite.Shared;

namespace RetailSuite.Modules.Catalog.Entities;

public class Product : TenantEntity
{
    public string Name { get; private set; }
    public string Description { get; private set; }
    public bool IsActive { get; private set; } = true;
    /// <summary>Relative URL of the product image, e.g. /uploads/{tenantId}/abc.jpg</summary>
    public string? ImageUrl { get; private set; }

    private readonly List<ProductVariant> _variants = new();
    public IReadOnlyCollection<ProductVariant> Variants => _variants;

    private readonly List<ProductCategory> _categories = new();
    public IReadOnlyCollection<ProductCategory> Categories => _categories;

    private Product() { }

    public Product(string name, string? description)
    {
        Name = name;
        Description = description ?? "";
    }
    public void Update(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name required.");

        Name = name;
        Description = description ?? "";
    }

    public void SetImageUrl(string url) => ImageUrl = url;

    public void AddVariant(ProductVariant variant)
    {
        if (_variants.Any(v => v.SKU == variant.SKU))
            throw new InvalidOperationException("Duplicate SKU.");

        _variants.Add(variant);
    }
}