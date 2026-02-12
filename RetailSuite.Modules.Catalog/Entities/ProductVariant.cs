using RetailSuite.Shared;

namespace RetailSuite.Modules.Catalog.Entities;

public class ProductVariant : TenantEntity
{
    public Guid ProductId { get; private set; }
    public string SKU { get; private set; }
    public string? Barcode { get; private set; }
    public decimal Price { get; private set; }
    public bool IsActive { get; private set; } = true;

    private readonly List<VariantAttributeValue> _attributeValues = new();
    public IReadOnlyCollection<VariantAttributeValue> AttributeValues => _attributeValues;

    private ProductVariant() { }

    public ProductVariant(Guid productId, string sku, decimal price)
    {
        ProductId = productId;
        SKU = sku;
        Price = price;
    }
}