namespace RetailSuite.Modules.Catalog.Dtos;

public class CreateProductRequest
{
    public string Name { get; set; }
    public string? Description { get; set; }
}

public class UpdateProductRequest
{
    public string Name { get; set; }
    public string? Description { get; set; }
}