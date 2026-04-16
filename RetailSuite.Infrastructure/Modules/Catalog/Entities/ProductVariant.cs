using RetailSuite.Shared;

namespace RetailSuite.Modules.Catalog.Entities;

public class ProductVariant : TenantEntity
{
    public Guid ProductId { get; private set; }
    public string SKU { get; private set; }
    public string? Barcode { get; private set; }
    public decimal Price { get; private set; }
    public decimal CostPrice { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int StockQuantity { get; set; }
    public decimal AverageCost { get; set; }
    public Product Product { get; set; }

    private readonly List<VariantAttributeValue> _attributeValues = new();
    public IReadOnlyCollection<VariantAttributeValue> AttributeValues => _attributeValues;

    private ProductVariant() { }
    public ProductVariant(Guid productId, string sku, decimal price, decimal costPrice)
    {
        ProductId = productId;
        SKU = sku;
        Price = price;
        CostPrice = costPrice;
    }
    public ProductVariant(
      Guid productId,
      string sku,
      decimal price)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("SKU required.");

        ProductId = productId;
        SKU = sku;
        Price = price;
    }
    public void UpdatePrice(decimal price)
    {
        if (price < 0)
            throw new ArgumentException("Invalid price.");

        Price = price;
    }
}