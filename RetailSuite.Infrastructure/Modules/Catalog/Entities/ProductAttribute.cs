using RetailSuite.Shared;

namespace RetailSuite.Modules.Catalog.Entities;

public class ProductAttribute : TenantEntity
{
    public string Name { get; private set; }

    private ProductAttribute() { }

    public ProductAttribute(string name)
    {
        Name = name;
    }
}