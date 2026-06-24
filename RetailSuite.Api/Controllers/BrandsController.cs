using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Modules.Catalog.Entities;
using RetailSuite.Shared;
using RetailSuite.Api.Authorization;
using RetailSuite.Infrastructure.Modules.Identity.Entities;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// CRUD for product brands. A brand belongs to one tenant; a product may
/// reference one brand. Slug must be unique per tenant — auto-generated
/// from the brand name when not supplied.
/// </summary>
[ApiController]
[Route("api/brands")]
[RequirePermission(Permissions.Brands)]
public class BrandsController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly ITenantContext _tenantContext;

    public BrandsController(RetailDbContext db, ITenantContext tenantContext)
    {
        _db            = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? active)
    {
        var q = _db.Brands.AsQueryable();
        if (active.HasValue) q = q.Where(b => b.IsActive == active.Value);

        var rows = await q
            .OrderBy(b => b.Name)
            .Select(b => new
            {
                b.Id, b.Name, b.Slug, b.Description, b.LogoUrl, b.IsActive,
                ProductCount = _db.Products.Count(p => p.BrandId == b.Id)
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(rows));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var b = await _db.Brands.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound(ApiResponse<object>.Fail("Brand not found."));
        return Ok(ApiResponse<object>.Ok(new
        {
            b.Id, b.Name, b.Slug, b.Description, b.LogoUrl, b.IsActive
        }));
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] BrandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse<object>.Fail("Name is required."));

        var tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");

        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? Product.Slugify(request.Name)
            : Product.Slugify(request.Slug);
        slug = await EnsureUniqueSlugAsync(slug, excludeId: null);

        var brand = new Brand(tenantId, request.Name, slug, request.Description, request.LogoUrl);
        _db.Brands.Add(brand);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new { brand.Id, brand.Name, brand.Slug }));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(Guid id, [FromBody] BrandRequest request)
    {
        var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == id);
        if (brand == null) return NotFound(ApiResponse<object>.Fail("Brand not found."));

        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? brand.Slug
            : Product.Slugify(request.Slug);
        if (!string.Equals(slug, brand.Slug, StringComparison.OrdinalIgnoreCase))
            slug = await EnsureUniqueSlugAsync(slug, excludeId: brand.Id);

        brand.Update(request.Name, slug, request.Description, request.LogoUrl);
        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value) brand.Activate(); else brand.Deactivate();
        }
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new { brand.Id, brand.Name, brand.Slug }));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == id);
        if (brand == null) return NotFound(ApiResponse<object>.Fail("Brand not found."));

        // Products keep their snapshot of the name in catalog history via BrandId;
        // the FK is SetNull on delete (configured in DbContext) so deletion is safe.
        // But for now prefer soft-disable to keep cards consistent.
        brand.Deactivate();
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { Deactivated = id }));
    }

    private async Task<string> EnsureUniqueSlugAsync(string candidate, Guid? excludeId)
    {
        var slug = candidate;
        var n = 1;
        while (await _db.Brands.AnyAsync(b => b.Slug == slug && (excludeId == null || b.Id != excludeId)))
        {
            n++;
            slug = $"{candidate}-{n}";
        }
        return slug;
    }
}

public class BrandRequest
{
    public string  Name        { get; set; } = string.Empty;
    public string? Slug        { get; set; }
    public string? Description { get; set; }
    public string? LogoUrl     { get; set; }
    public bool?   IsActive    { get; set; }
}
