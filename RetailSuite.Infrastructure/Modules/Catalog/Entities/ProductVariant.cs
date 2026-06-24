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
    /// <summary>Tax rate as a fraction (e.g. 0.17 = 17% GST). Defaults to 0.</summary>
    public decimal TaxRate { get; private set; } = 0;
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

    public void SetTaxRate(decimal rate)
    {
        if (rate < 0 || rate > 1)
            throw new ArgumentException("Tax rate must be between 0 and 1 (e.g. 0.17 for 17%).");

        TaxRate = rate;
    }

    public void SetBarcode(string? barcode)
    {
        Barcode = barcode;
    }

    public void SetCostPrice(decimal cost)
    {
        if (cost < 0) throw new ArgumentException("Cost cannot be negative.");
        CostPrice = cost;
    }

    /// <summary>
    /// Rename the SKU. Caller is responsible for ensuring uniqueness within the tenant
    /// (the DbContext has a unique index that will throw otherwise).
    /// </summary>
    public void SetSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("SKU required.");
        SKU = sku.Trim();
    }

    public void Activate()   => IsActive = true;
    public void Deactivate() => IsActive = false;
}