namespace RetailSuite.Modules.Catalog.Dtos;

public class CreateVariantRequest
{
    public string SKU { get; set; }
    public decimal Price { get; set; }

    public List<Guid> AttributeValueIds { get; set; } = new();
}