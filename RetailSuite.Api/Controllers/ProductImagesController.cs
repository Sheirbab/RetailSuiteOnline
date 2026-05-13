using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Images.Dtos;
using RetailSuite.Infrastructure.Modules.Images.Services;
using RetailSuite.Modules.Catalog.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Multi-image gallery for products. The primary image's URL is also denormalised onto
/// <c>Product.ImageUrl</c> so the existing POS / catalog read paths keep working.
/// </summary>
[ApiController]
[Route("api/products/{productId:guid}/images")]
[Authorize(Policy = "StaffOrAdmin")]
public class ProductImagesController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly IImageStorageService _storage;
    private readonly IImageValidationService _validation;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ProductImagesController> _logger;

    public ProductImagesController(
        RetailDbContext db,
        IImageStorageService storage,
        IImageValidationService validation,
        ITenantContext tenantContext,
        ILogger<ProductImagesController> logger)
    {
        _db          = db;
        _storage     = storage;
        _validation  = validation;
        _tenantContext = tenantContext;
        _logger      = logger;
    }

    // -------------------------------------------------------------
    // GET /api/products/{productId}/images
    // -------------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> List(Guid productId)
    {
        await EnsureProductExistsAsync(productId);

        var images = await _db.ProductImages
            .Where(i => i.ProductId == productId)
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.CreatedAt)
            .Select(i => i.ToResponse())
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(images));
    }

    // -------------------------------------------------------------
    // POST /api/products/{productId}/images  (multipart/form-data)
    // -------------------------------------------------------------
    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]   // 20 MB host cap; service enforces the real 5 MB.
    public async Task<IActionResult> Upload(Guid productId, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("No file uploaded."));

        var product = await EnsureProductExistsAsync(productId);

        // Validate format + size by inspecting magic bytes, not just the file extension.
        await using var stream = file.OpenReadStream();
        var validation = await _validation.ValidateAsync(stream, file.Length);
        if (!validation.IsValid)
            return BadRequest(ApiResponse<object>.Fail(validation.Reason ?? "Invalid image."));

        // Rewind for storage.
        if (stream.CanSeek) stream.Position = 0;
        var relativePath = await _storage.SaveAsync(
            tenantId:   RequireTenantId(),
            productId:  productId,
            content:    stream,
            extension:  validation.DetectedExtension ?? "jpg");

        // Decide primary + sort order.
        var existingCount  = await _db.ProductImages.CountAsync(i => i.ProductId == productId);
        var hasAnyPrimary  = await _db.ProductImages.AnyAsync(i => i.ProductId == productId && i.IsPrimary);
        var isPrimary      = !hasAnyPrimary;   // first image becomes primary
        var sortOrder      = existingCount;

        var image = new ProductImage(
            productId:     productId,
            relativePath:  relativePath,
            mimeType:      validation.DetectedMimeType ?? file.ContentType,
            fileSizeBytes: file.Length,
            sortOrder:     sortOrder,
            isPrimary:     isPrimary);

        _db.ProductImages.Add(image);

        // Keep Product.ImageUrl in sync when we just set a new primary.
        if (isPrimary)
            product.SetImageUrl(relativePath);

        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(image.ToResponse()));
    }

    // -------------------------------------------------------------
    // DELETE /api/products/{productId}/images/{imageId}
    // -------------------------------------------------------------
    [HttpDelete("{imageId:guid}")]
    public async Task<IActionResult> Delete(Guid productId, Guid imageId)
    {
        var product = await EnsureProductExistsAsync(productId);

        var image = await _db.ProductImages
            .FirstOrDefaultAsync(i => i.Id == imageId && i.ProductId == productId);
        if (image == null)
            return NotFound(ApiResponse<object>.Fail("Image not found."));

        var wasPrimary = image.IsPrimary;
        var oldPath    = image.RelativePath;

        _db.ProductImages.Remove(image);
        await _db.SaveChangesAsync();

        // Best-effort file delete — happens after DB commit so a file system hiccup
        // never leaves an orphan row.
        await _storage.DeleteAsync(oldPath);

        // If the deleted image was primary, promote the next image (if any) and refresh Product.ImageUrl.
        if (wasPrimary)
        {
            var nextPrimary = await _db.ProductImages
                .Where(i => i.ProductId == productId)
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.CreatedAt)
                .FirstOrDefaultAsync();

            if (nextPrimary != null)
            {
                nextPrimary.SetPrimary(true);
                product.SetImageUrl(nextPrimary.RelativePath);
            }
            else
            {
                product.SetImageUrl(string.Empty);
            }

            await _db.SaveChangesAsync();
        }

        return Ok(ApiResponse<object>.Ok(new { Deleted = imageId }));
    }

    // -------------------------------------------------------------
    // PATCH /api/products/{productId}/images/{imageId}/primary
    // -------------------------------------------------------------
    [HttpPatch("{imageId:guid}/primary")]
    public async Task<IActionResult> SetPrimary(Guid productId, Guid imageId)
    {
        var product = await EnsureProductExistsAsync(productId);

        var target = await _db.ProductImages
            .FirstOrDefaultAsync(i => i.Id == imageId && i.ProductId == productId);
        if (target == null)
            return NotFound(ApiResponse<object>.Fail("Image not found."));

        if (target.IsPrimary)
            return Ok(ApiResponse<object>.Ok(target.ToResponse()));

        var others = await _db.ProductImages
            .Where(i => i.ProductId == productId && i.IsPrimary)
            .ToListAsync();
        foreach (var o in others) o.SetPrimary(false);

        target.SetPrimary(true);
        product.SetImageUrl(target.RelativePath);

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(target.ToResponse()));
    }

    // -------------------------------------------------------------
    // PATCH /api/products/{productId}/images/reorder
    // -------------------------------------------------------------
    [HttpPatch("reorder")]
    public async Task<IActionResult> Reorder(Guid productId, [FromBody] ReorderImagesRequest request)
    {
        await EnsureProductExistsAsync(productId);

        if (request.ImageIds == null || request.ImageIds.Count == 0)
            return BadRequest(ApiResponse<object>.Fail("ImageIds must not be empty."));

        var images = await _db.ProductImages
            .Where(i => i.ProductId == productId)
            .ToListAsync();
        var byId = images.ToDictionary(i => i.Id);

        // Validate that every ID belongs to this product.
        foreach (var id in request.ImageIds)
        {
            if (!byId.ContainsKey(id))
                return BadRequest(ApiResponse<object>.Fail($"Image {id} does not belong to product {productId}."));
        }

        for (int idx = 0; idx < request.ImageIds.Count; idx++)
        {
            byId[request.ImageIds[idx]].SetSortOrder(idx);
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { Reordered = request.ImageIds.Count }));
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------
    private async Task<Product> EnsureProductExistsAsync(Guid productId)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null)
            throw new NotFoundException("Product", productId);
        return product;
    }

    private Guid RequireTenantId()
    {
        return _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");
    }
}
