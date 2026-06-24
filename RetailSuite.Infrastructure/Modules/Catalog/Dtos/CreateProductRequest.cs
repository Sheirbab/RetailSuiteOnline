namespace RetailSuite.Modules.Catalog.Dtos;

public class CreateProductRequest
{
    public string  Name             { get; set; } = string.Empty;
    public string? Description      { get; set; }
    public string? ShortDescription { get; set; }
    public string? Slug             { get; set; }
    public Guid?   BrandId          { get; set; }
    public string? UnitOfMeasure    { get; set; }
    /// <summary>JSON array of spec key/value pairs. Stored as-is.</summary>
    public string? Specs            { get; set; }
    /// <summary>Comma-separated tags.</summary>
    public string? Tags             { get; set; }
}

public class UpdateProductRequest
{
    public string  Name             { get; set; } = string.Empty;
    public string? Description      { get; set; }
    public string? ShortDescription { get; set; }
    public string? Slug             { get; set; }
    public Guid?   BrandId          { get; set; }
    public string? UnitOfMeasure    { get; set; }
    public string? Specs            { get; set; }
    public string? Tags             { get; set; }
    public bool?   IsActive         { get; set; }
}
