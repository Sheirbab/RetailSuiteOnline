using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Catalog.Services;
using RetailSuite.Infrastructure.Modules.Subscriptions.Services;
using RetailSuite.Modules.Catalog.Dtos;
using RetailSuite.Modules.Catalog.Entities;
using RetailSuite.Shared;
using RetailSuite.Infrastructure.Modules.Identity;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Api.Authorization;

[RequirePermission(Permissions.Products)]
[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ICurrentUserContext _currentUser;
    private readonly IEntitlementService _entitlements;
    private readonly IHtmlSanitizerService _htmlSanitizer;

    public ProductsController(
        RetailDbContext db,
        IWebHostEnvironment env,
        ICurrentUserContext currentUser,
        IEntitlementService entitlements,
        IHtmlSanitizerService htmlSanitizer)
    {
        _db = db;
        _env = env;
        _currentUser = currentUser;
        _entitlements = entitlements;
        _htmlSanitizer = htmlSanitizer;
    }

    // GET /api/products?page=1&pageSize=20
    [HttpGet]
    public async Task<IActionResult> Get(int page = 1, int pageSize = 20)
    {
        var query = _db.Products.Include(p => p.Variants).AsQueryable();
        var total = await query.CountAsync();
        var items = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new ApiResponse<object>(true, null, new
        {
            Total    = total,
            Page     = page,
            PageSize = pageSize,
            Items    = items
        }));
    }

    // GET /api/products/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
            return NotFound(ApiResponse<object>.Fail("Product not found."));

        return Ok(new ApiResponse<object>(true, null, product));
    }

    // GET /api/products/variants  — flat list for POS
    // Includes tax rate, product image, and primary category so the POS tile grid
    // can render visually and apply the category filter without extra round-trips.
    [HttpGet("variants")]
    public async Task<IActionResult> GetVariants()
    {
        var variants = await _db.ProductVariants
            .Include(v => v.Product)
            .Where(v => v.IsActive)
            .Select(v => new
            {
                v.Id,
                v.SKU,
                v.Barcode,
                v.Price,
                v.CostPrice,
                v.StockQuantity,
                v.TaxRate,
                ProductId    = v.ProductId,
                ProductName  = v.Product.Name,
                ImageUrl     = v.Product.ImageUrl,
                CategoryId   = _db.ProductCategories
                                  .Where(pc => pc.ProductId == v.ProductId)
                                  .Select(pc => (Guid?)pc.CategoryId)
                                  .FirstOrDefault(),
                CategoryName = _db.ProductCategories
                                  .Where(pc => pc.ProductId == v.ProductId)
                                  .Select(pc => pc.Category.Name)
                                  .FirstOrDefault()
            })
            .ToListAsync();

        return Ok(new ApiResponse<object>(true, null, variants));
    }

    // GET /api/products/search
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        string? keyword,
        Guid? categoryId,
        int page = 1,
        int pageSize = 20)
    {
        var query = _db.Products
            .Include(p => p.Variants)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(p => p.Name.Contains(keyword));

        if (categoryId.HasValue)
            query = query.Where(p =>
                _db.ProductCategories.Any(pc =>
                    pc.ProductId == p.Id &&
                    pc.CategoryId == categoryId.Value));

        var total = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new ApiResponse<object>(true, null, new
        {
            Total    = total,
            Page     = page,
            PageSize = pageSize,
            Items    = items
        }));
    }

    // POST /api/products
    [HttpPost]
    public async Task<IActionResult> Create(CreateProductRequest request)
    {
        // Plan-limit enforcement — MaxProducts on the active plan.
        var quota = await _entitlements.CanAddProductAsync(_currentUser.TenantId);
        if (!quota.Allowed)
        {
            return StatusCode(StatusCodes.Status402PaymentRequired,
                new ApiResponse<object>(false, quota.Reason, new
                {
                    quota.CurrentCount,
                    quota.Limit
                }));
        }

        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? Product.Slugify(request.Name)
            : Product.Slugify(request.Slug);
        slug = await EnsureUniqueSlugAsync(slug, excludeProductId: null);

        // Sanitize the HTML description before we store it — strips scripts, event
        // handlers, disallowed schemes; keeps formatting (p, h2, strong, lists, …).
        var safeDescription = _htmlSanitizer.Sanitize(request.Description);

        var product = new Product(request.Name, safeDescription, slug);
        if (request.ShortDescription != null) product.SetShortDescription(request.ShortDescription);
        if (request.BrandId.HasValue)         product.SetBrand(request.BrandId);
        if (!string.IsNullOrWhiteSpace(request.UnitOfMeasure)) product.SetUnitOfMeasure(request.UnitOfMeasure);
        if (request.Specs != null)            product.SetSpecs(request.Specs);
        if (request.Tags != null)             product.SetTags(request.Tags);

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        return Ok(new ApiResponse<Guid>(true, "Product created.", product.Id));
    }

    // PUT /api/products/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, UpdateProductRequest request)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
            return NotFound(ApiResponse<object>.Fail("Product not found."));

        var safeDescription = _htmlSanitizer.Sanitize(request.Description);
        product.Update(request.Name, safeDescription);
        if (request.ShortDescription != null) product.SetShortDescription(request.ShortDescription);
        if (request.BrandId.HasValue)         product.SetBrand(request.BrandId);
        if (!string.IsNullOrWhiteSpace(request.UnitOfMeasure)) product.SetUnitOfMeasure(request.UnitOfMeasure);
        if (request.Specs != null)            product.SetSpecs(request.Specs);
        if (request.Tags != null)             product.SetTags(request.Tags);

        // Slug update is optional — only run uniqueness check if the caller changed it.
        if (!string.IsNullOrWhiteSpace(request.Slug)
            && !string.Equals(request.Slug, product.Slug, StringComparison.OrdinalIgnoreCase))
        {
            var slug = await EnsureUniqueSlugAsync(Product.Slugify(request.Slug), excludeProductId: product.Id);
            product.SetSlug(slug);
        }

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value) product.Activate(); else product.Deactivate();
        }

        await _db.SaveChangesAsync();

        return Ok(new ApiResponse<object>(true, "Product updated.", null));
    }

    // Ensures the candidate slug is unique within this tenant; appends -2, -3, …
    // if a collision exists. Pass excludeProductId when updating an existing row.
    private async Task<string> EnsureUniqueSlugAsync(string candidate, Guid? excludeProductId)
    {
        var slug = candidate;
        var n = 1;
        while (await _db.Products
                       .AnyAsync(p => p.Slug == slug
                                   && (excludeProductId == null || p.Id != excludeProductId)))
        {
            n++;
            slug = $"{candidate}-{n}";
        }
        return slug;
    }

    // POST /api/products/{productId}/variants
    [HttpPost("{productId}/variants")]
    public async Task<IActionResult> AddVariant(Guid productId, CreateVariantRequest request)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product == null)
            return NotFound(ApiResponse<object>.Fail("Product not found."));

        var variant = new ProductVariant(productId, request.SKU, request.Price);
        if (request.TaxRate > 0) variant.SetTaxRate(request.TaxRate);

        // Auto-fill Barcode = SKU when caller didn't supply one (the common case).
        // Code128 accepts arbitrary printable ASCII, so the SKU is a safe default.
        // Stored on the entity so labels and scanning resolve to a stable value.
        variant.SetBarcode(request.SKU);

        product.AddVariant(variant);
        // Add to the DbSet explicitly so EF unambiguously tracks the entity as
        // Added. Without this, on EF InMemory the change tracker sometimes flags
        // the variant as Modified (because its Id was already set by the
        // BaseEntity property initializer), which throws DbUpdateConcurrencyException
        // on SaveChanges. SQL Server tolerates the pure navigation-add pattern
        // but InMemory does not — the generate-variants endpoint uses the same
        // belt-and-braces pattern for the same reason.
        _db.ProductVariants.Add(variant);
        await _db.SaveChangesAsync();

        return Ok(new ApiResponse<Guid>(true, "Variant added.", variant.Id));
    }

    // POST /api/products/{id}/image
    [HttpPost("{id}/image")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("No file provided."));

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!allowedExtensions.Contains(ext))
            return BadRequest(ApiResponse<object>.Fail("Only jpg, png, and webp images are allowed."));

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(ApiResponse<object>.Fail("Image must be under 5 MB."));

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product == null)
            return NotFound(ApiResponse<object>.Fail("Product not found."));

        var tenantId = _currentUser.TenantId;
        var uploadDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", tenantId.ToString());
        Directory.CreateDirectory(uploadDir);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var filePath  = Path.Combine(uploadDir, fileName);

        await using (var stream = System.IO.File.Create(filePath))
            await file.CopyToAsync(stream);

        var relativeUrl = $"/uploads/{tenantId}/{fileName}";
        product.SetImageUrl(relativeUrl);
        await _db.SaveChangesAsync();

        return Ok(new ApiResponse<string>(true, "Image uploaded.", relativeUrl));
    }

    // POST /api/products/{productId}/categories/{categoryId}
    [HttpPost("{productId}/categories/{categoryId}")]
    public async Task<IActionResult> AssignCategory(Guid productId, Guid categoryId)
    {
        var exists = await _db.ProductCategories
            .AnyAsync(pc => pc.ProductId == productId && pc.CategoryId == categoryId);

        if (!exists)
        {
            _db.ProductCategories.Add(new ProductCategory(productId, categoryId));
            await _db.SaveChangesAsync();
        }

        return Ok(new ApiResponse<object>(true, "Category assigned.", null));
    }

    // GET /api/products/{id}/categories — current category assignments
    [HttpGet("{id:guid}/categories")]
    public async Task<IActionResult> GetCategories(Guid id)
    {
        var rows = await _db.ProductCategories
            .Where(pc => pc.ProductId == id)
            .Select(pc => new { pc.CategoryId, CategoryName = pc.Category.Name })
            .ToListAsync();

        return Ok(new ApiResponse<object>(true, null, rows));
    }

    // POST /api/products/{id}/categories/replace — replace the entire category set
    [HttpPost("{id:guid}/categories/replace")]
    public async Task<IActionResult> ReplaceCategories(Guid id, [FromBody] ReplaceCategoriesRequest request)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product == null)
            return NotFound(ApiResponse<object>.Fail("Product not found."));

        var existing = await _db.ProductCategories
            .Where(pc => pc.ProductId == id)
            .ToListAsync();

        var desired = request.CategoryIds?.Distinct().ToHashSet() ?? new HashSet<Guid>();

        // Remove links no longer wanted
        var toRemove = existing.Where(e => !desired.Contains(e.CategoryId)).ToList();
        _db.ProductCategories.RemoveRange(toRemove);

        // Add new links
        var existingIds = existing.Select(e => e.CategoryId).ToHashSet();
        foreach (var newId in desired.Where(d => !existingIds.Contains(d)))
        {
            // Validate the category exists before linking
            var exists = await _db.Categories.AnyAsync(c => c.Id == newId);
            if (!exists) continue;
            _db.ProductCategories.Add(new ProductCategory(id, newId));
        }

        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object>(true, "Categories updated.", new { Count = desired.Count }));
    }

    // DELETE /api/products/{productId}/categories/{categoryId}
    [HttpDelete("{productId:guid}/categories/{categoryId:guid}")]
    public async Task<IActionResult> UnassignCategory(Guid productId, Guid categoryId)
    {
        var link = await _db.ProductCategories
            .FirstOrDefaultAsync(pc => pc.ProductId == productId && pc.CategoryId == categoryId);
        if (link == null) return Ok(new ApiResponse<object>(true, "Already not assigned.", null));

        _db.ProductCategories.Remove(link);
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object>(true, "Category removed.", null));
    }

    // PUT /api/products/variants/{variantId} — update price/cost/tax/sku
    [HttpPut("variants/{variantId:guid}")]
    public async Task<IActionResult> UpdateVariant(Guid variantId, [FromBody] UpdateVariantRequest request)
    {
        var v = await _db.ProductVariants.FirstOrDefaultAsync(x => x.Id == variantId);
        if (v == null) return NotFound(ApiResponse<object>.Fail("Variant not found."));

        if (!string.IsNullOrWhiteSpace(request.Sku)
            && !string.Equals(request.Sku, v.SKU, StringComparison.OrdinalIgnoreCase))
        {
            // Uniqueness check before hitting the index.
            if (await _db.ProductVariants.AnyAsync(x => x.SKU == request.Sku && x.Id != v.Id))
                return Conflict(ApiResponse<object>.Fail($"SKU '{request.Sku}' is already in use."));
            v.SetSku(request.Sku);
            // Default the barcode to follow the SKU if it was tracking it before.
            if (string.IsNullOrEmpty(v.Barcode) || v.Barcode == v.SKU)
                v.SetBarcode(request.Sku);
        }
        if (request.Price     .HasValue) v.UpdatePrice(request.Price.Value);
        if (request.CostPrice .HasValue) v.SetCostPrice(request.CostPrice.Value);
        if (request.TaxRate   .HasValue) v.SetTaxRate(request.TaxRate.Value);
        if (request.IsActive  .HasValue)
        {
            if (request.IsActive.Value) v.Activate(); else v.Deactivate();
        }
        if (request.Barcode != null) v.SetBarcode(request.Barcode);

        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object>(true, "Variant updated.",
            new { v.Id, v.SKU, v.Price, v.CostPrice, v.TaxRate, v.IsActive, v.Barcode }));
    }

    // DELETE /api/products/variants/{variantId} — soft (deactivate); refuses if active orders use it
    [HttpDelete("variants/{variantId:guid}")]
    public async Task<IActionResult> DeleteVariant(Guid variantId)
    {
        var v = await _db.ProductVariants.FirstOrDefaultAsync(x => x.Id == variantId);
        if (v == null) return NotFound(ApiResponse<object>.Fail("Variant not found."));

        // If it's been sold, soft delete only (history references this SKU).
        var hasSales = await _db.OrderItems.AnyAsync(o => o.ProductVariantId == variantId);
        if (hasSales)
        {
            v.Deactivate();
            await _db.SaveChangesAsync();
            return Ok(new ApiResponse<object>(true, "Variant hidden (has historical sales).",
                new { Deactivated = variantId }));
        }

        // Otherwise wipe its attribute links + the variant itself.
        var links = await _db.VariantAttributeValues.Where(x => x.ProductVariantId == variantId).ToListAsync();
        _db.VariantAttributeValues.RemoveRange(links);
        _db.ProductVariants.Remove(v);
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object>(true, "Variant removed.", new { Deleted = variantId }));
    }

    // ================================================================
    // POST /api/products/{id}/variants/generate
    //
    // Cross-product of selected attribute-value sets into variants.
    // Pass selections grouped by attribute (e.g. Size: [M,L], Color: [Red,Blue])
    // → produces M-Red, M-Blue, L-Red, L-Blue. SKUs already in use are skipped
    // so the call is idempotent: running again only fills gaps.
    // ================================================================
    [HttpPost("{id:guid}/variants/generate")]
    public async Task<IActionResult> GenerateVariants(Guid id, [FromBody] GenerateVariantsRequest request)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound(ApiResponse<object>.Fail("Product not found."));

        if (request.AttributeSelections == null || request.AttributeSelections.Count == 0)
            return BadRequest(ApiResponse<object>.Fail("Pick at least one attribute with at least one value."));

        // Validate every value belongs to its claimed attribute, and pull labels for SKU naming.
        var allValueIds = request.AttributeSelections.SelectMany(s => s.ValueIds).Distinct().ToList();
        var valueRows = await _db.ProductAttributeValues
            .Where(v => allValueIds.Contains(v.Id))
            .Select(v => new { v.Id, v.AttributeId, v.Value })
            .ToListAsync();
        var valueLookup = valueRows.ToDictionary(v => v.Id);

        // Build per-attribute lists of (id, label) in caller order.
        var dimensions = new List<List<(Guid Id, string Label)>>();
        foreach (var sel in request.AttributeSelections)
        {
            var row = sel.ValueIds
                .Where(vid => valueLookup.ContainsKey(vid)
                           && valueLookup[vid].AttributeId == sel.AttributeId)
                .Select(vid => (vid, valueLookup[vid].Value))
                .ToList();
            if (row.Count == 0)
                return BadRequest(ApiResponse<object>.Fail("One of the attribute selections is empty or invalid."));
            dimensions.Add(row);
        }

        // Cartesian product across dimensions.
        IEnumerable<List<(Guid Id, string Label)>> Cartesian(List<List<(Guid, string)>> dims)
        {
            IEnumerable<List<(Guid, string)>> acc = new[] { new List<(Guid, string)>() };
            foreach (var dim in dims)
                acc = acc.SelectMany(prefix => dim.Select(d => prefix.Concat(new[] { d }).ToList()));
            return acc;
        }

        var skuPrefix = (request.SkuPrefix ?? product.Slug ?? "p").Trim();
        if (string.IsNullOrEmpty(skuPrefix)) skuPrefix = "p";
        var basePrice = request.Price ?? 0m;
        var baseCost  = request.CostPrice ?? 0m;
        var taxRate   = request.TaxRate ?? 0m;

        int created = 0, skipped = 0;
        foreach (var combo in Cartesian(dimensions))
        {
            var suffix = string.Join("-", combo.Select(c => Slugify(c.Label).ToUpperInvariant()));
            var sku    = string.IsNullOrEmpty(suffix) ? skuPrefix : $"{skuPrefix}-{suffix}";

            // Idempotent: existing SKU on this product (or any product in the tenant)? Skip.
            if (await _db.ProductVariants.AnyAsync(v => v.SKU == sku))
            {
                skipped++;
                continue;
            }

            var variant = new ProductVariant(id, sku, basePrice, baseCost);
            if (taxRate > 0) variant.SetTaxRate(taxRate);
            variant.SetBarcode(sku);

            product.AddVariant(variant);
            _db.ProductVariants.Add(variant);
            await _db.SaveChangesAsync(); // need variant.Id for the link rows below

            foreach (var (valueId, _) in combo)
            {
                _db.VariantAttributeValues.Add(new VariantAttributeValue
                {
                    ProductVariantId        = variant.Id,
                    ProductAttributeValueId = valueId
                });
            }
            created++;
        }
        await _db.SaveChangesAsync();

        return Ok(new ApiResponse<object>(true,
            $"Generated {created} variant(s); {skipped} already existed.",
            new { Created = created, Skipped = skipped }));

        // Local slug helper — strips non-alnum, lowercases.
        static string Slugify(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var ch in s.ToLowerInvariant())
                if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            return sb.ToString();
        }
    }
}

public class GenerateVariantsRequest
{
    /// <summary>Picks per attribute. Cartesian product across all groups produces the variant set.</summary>
    public List<AttributeSelection> AttributeSelections { get; set; } = new();
    /// <summary>Optional override for the SKU base (defaults to product slug).</summary>
    public string?  SkuPrefix { get; set; }
    /// <summary>Default price applied to every generated variant; admin can adjust per row.</summary>
    public decimal? Price     { get; set; }
    public decimal? CostPrice { get; set; }
    /// <summary>Tax rate as fraction (e.g. 0.17 for 17%).</summary>
    public decimal? TaxRate   { get; set; }
}

public class AttributeSelection
{
    public Guid       AttributeId { get; set; }
    public List<Guid> ValueIds    { get; set; } = new();
}

public class UpdateVariantRequest
{
    public string?   Sku       { get; set; }
    public decimal?  Price     { get; set; }
    public decimal?  CostPrice { get; set; }
    public decimal?  TaxRate   { get; set; }
    public string?   Barcode   { get; set; }
    public bool?     IsActive  { get; set; }
}

public class ReplaceCategoriesRequest
{
    public List<Guid>? CategoryIds { get; set; }
}
