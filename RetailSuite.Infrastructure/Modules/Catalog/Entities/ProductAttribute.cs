using RetailSuite.Shared;

namespace RetailSuite.Modules.Catalog.Entities;

/// <summary>
/// A variant-defining attribute (e.g. "Size", "Color"). Holds the labels customers
/// see on the storefront chip selector and the admin's "Generate variants" wizard.
/// Values live on <see cref="ProductAttributeValue"/>; each variant links to one
/// value per attribute through <see cref="VariantAttributeValue"/>.
/// </summary>
public class ProductAttribute : TenantEntity
{
    public string Name { get; private set; } = string.Empty;

    private ProductAttribute() { }

    public ProductAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required.");
        Name = name.Trim();
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required.");
        Name = name.Trim();
    }
}
