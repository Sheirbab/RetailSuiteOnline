namespace RetailSuite.Modules.Catalog.Entities;

public class ProductCategory
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; }

    public Guid CategoryId { get; set; }
    public Category Category { get; set; }
    public ProductCategory(Guid productId, Guid categoryId)
    {
        ProductId = productId;
        CategoryId = categoryId;
    }
}