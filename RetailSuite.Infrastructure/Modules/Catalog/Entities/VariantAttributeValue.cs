namespace RetailSuite.Modules.Catalog.Entities;

public class VariantAttributeValue
{
    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; }

    public Guid ProductAttributeValueId { get; set; }
    public ProductAttributeValue ProductAttributeValue { get; set; }
}