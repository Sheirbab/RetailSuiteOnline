namespace RetailSuite.Modules.Catalog.Dtos;

public class CreateVariantRequest
{
    public string SKU { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal CostPrice { get; set; }
    /// <summary>Tax rate as a fraction (e.g. 0.17 = 17%). Defaults to 0.</summary>
    public decimal TaxRate { get; set; } = 0;
    public List<Guid> AttributeValueIds { get; set; } = new();
}