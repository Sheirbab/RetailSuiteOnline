namespace RetailSuite.Modules.Catalog.Dtos;

public class ProductResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    public List<ProductVariantResponse> Variants { get; set; } = new();
}