using RetailSuite.Modules.Catalog.Entities;

namespace RetailSuite.Infrastructure.Modules.Images.Dtos;

public record ProductImageResponse(
    Guid    Id,
    Guid    ProductId,
    string  Url,
    string  MimeType,
    long    FileSizeBytes,
    int     SortOrder,
    bool    IsPrimary,
    DateTime CreatedAt);

public class ReorderImagesRequest
{
    /// <summary>
    /// Ordered list of image IDs. The first ID becomes SortOrder=0 and is set as primary
    /// (unless a different image is currently primary AND included in the list — then we
    /// preserve the current primary). To be unambiguous, set primary explicitly via the
    /// dedicated endpoint and use this only for ordering.
    /// </summary>
    public List<Guid> ImageIds { get; set; } = new();
}

public static class ProductImageMappers
{
    public static ProductImageResponse ToResponse(this ProductImage i) =>
        new(i.Id, i.ProductId, i.RelativePath, i.MimeType, i.FileSizeBytes, i.SortOrder, i.IsPrimary, i.CreatedAt);
}
