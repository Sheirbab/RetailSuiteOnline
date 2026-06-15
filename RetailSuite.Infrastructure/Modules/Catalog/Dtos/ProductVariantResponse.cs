namespace RetailSuite.Modules.Catalog.Dtos;

public class ProductVariantResponse
{
    public Guid Id { get; set; }
    public string SKU { get; set; }
    public decimal Price { get; set; }

    public List<string> Attributes { get; set; } = new();
}