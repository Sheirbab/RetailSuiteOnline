using RetailSuite.Shared;

namespace RetailSuite.Modules.Catalog.Entities;

public class Category : TenantEntity
{
    public string Name { get; private set; }
    public string Slug { get; private set; }

    public Guid? ParentCategoryId { get; private set; }

    private Category() { }

    public Category(string name, string slug, Guid? parentId)
    {
        Name = name;
        Slug = slug;
        ParentCategoryId = parentId;
    }
}