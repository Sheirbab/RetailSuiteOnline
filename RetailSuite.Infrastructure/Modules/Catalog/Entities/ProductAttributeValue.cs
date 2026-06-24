using RetailSuite.Shared;

namespace RetailSuite.Modules.Catalog.Entities;

/// <summary>
/// One option for a <see cref="ProductAttribute"/> — e.g. "Red" for the "Color" attribute.
/// </summary>
public class ProductAttributeValue : TenantEntity
{
    public Guid   AttributeId { get; private set; }
    public string Value       { get; private set; } = string.Empty;

    private ProductAttributeValue() { }

    public ProductAttributeValue(Guid attributeId, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value required.");
        AttributeId = attributeId;
        Value       = value.Trim();
    }

    public void SetValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value required.");
        Value = value.Trim();
    }
}
