using RetailSuite.Shared;

namespace RetailSuite.Modules.Catalog.Entities;

public class ProductAttributeValue : TenantEntity
{
    public Guid AttributeId { get; private set; }
    public string Value { get; private set; }

    private ProductAttributeValue() { }

    public ProductAttributeValue(Guid attributeId, string value)
    {
        AttributeId = attributeId;
        Value = value;
    }
}