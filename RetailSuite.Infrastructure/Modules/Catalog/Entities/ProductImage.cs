using RetailSuite.Shared;

namespace RetailSuite.Modules.Catalog.Entities;

/// <summary>
/// One image attached to a product. A product may have many; exactly one is marked primary.
/// The primary image's <see cref="RelativePath"/> is also denormalised onto
/// <c>Product.ImageUrl</c> so existing UI code that reads ImageUrl continues to work.
/// </summary>
public class ProductImage : TenantEntity
{
    public Guid ProductId { get; private set; }

    /// <summary>Web-relative path served by static files, e.g. "/uploads/{tenantId}/products/{guid}.jpg".</summary>
    public string RelativePath { get; private set; } = string.Empty;

    public string MimeType { get; private set; } = string.Empty;

    public long FileSizeBytes { get; private set; }

    /// <summary>Lower number = displayed first.</summary>
    public int SortOrder { get; private set; }

    /// <summary>Exactly one image per product must be primary; enforced by service code.</summary>
    public bool IsPrimary { get; private set; }

    private ProductImage() { }

    public ProductImage(
        Guid productId,
        string relativePath,
        string mimeType,
        long fileSizeBytes,
        int sortOrder,
        bool isPrimary)
    {
        Id            = Guid.NewGuid();
        CreatedAt     = DateTime.UtcNow;
        ProductId     = productId;
        RelativePath  = relativePath;
        MimeType      = mimeType;
        FileSizeBytes = fileSizeBytes;
        SortOrder     = sortOrder;
        IsPrimary     = isPrimary;
    }

    public void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;
    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;
}
