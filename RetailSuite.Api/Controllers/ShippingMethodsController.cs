using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Shipping.Entities;
using RetailSuite.Infrastructure.Seeders;
using RetailSuite.Shared;
using RetailSuite.Api.Authorization;
using RetailSuite.Infrastructure.Modules.Identity.Entities;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Tenant-admin CRUD for shipping methods used by the online storefront.
/// The public storefront endpoint lives at /api/shop/shipping-methods (read-only,
/// filtered to IsActive only) — this controller is the configuration surface.
/// </summary>
[ApiController]
[Route("api/shipping-methods")]
[RequirePermission(Permissions.ShippingMethods)]
public class ShippingMethodsController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly ITenantContext _tenantContext;

    public ShippingMethodsController(RetailDbContext db, ITenantContext tenantContext)
    {
        _db            = db;
        _tenantContext = tenantContext;
    }

    // -------------------------------------------------------------
    // GET /api/shipping-methods?active=true
    // -------------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? active)
    {
        var q = _db.ShippingMethods.AsQueryable();
        if (active.HasValue) q = q.Where(m => m.IsActive == active.Value);

        var rows = await q
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Name)
            .Select(m => ToResponse(m))
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(rows));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var m = await _db.ShippingMethods.FirstOrDefaultAsync(x => x.Id == id);
        if (m == null)
            return NotFound(ApiResponse<object>.Fail("Shipping method not found."));
        return Ok(ApiResponse<object>.Ok(ToResponse(m)));
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateShippingMethodRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse<object>.Fail("Code and Name are required."));

        var tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");

        var code = request.Code.Trim().ToUpperInvariant();
        var exists = await _db.ShippingMethods.AnyAsync(m => m.Code == code);
        if (exists)
            return Conflict(ApiResponse<object>.Fail($"A shipping method with code '{code}' already exists."));

        var method = new ShippingMethod(tenantId, code, request.Name.Trim(),
                                        baseFee: request.BaseFee, isPickup: request.IsPickup);
        method.Update(
            name:           null,
            description:    request.Description,
            baseFee:        request.BaseFee,
            freeOverAmount: request.FreeOverAmount,
            isActive:       request.IsActive ?? true,
            sortOrder:      request.SortOrder ?? 0,
            eta:            request.Eta);

        _db.ShippingMethods.Add(method);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(ToResponse(method)));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateShippingMethodRequest request)
    {
        var m = await _db.ShippingMethods.FirstOrDefaultAsync(x => x.Id == id);
        if (m == null)
            return NotFound(ApiResponse<object>.Fail("Shipping method not found."));

        m.Update(
            name:           request.Name,
            description:    request.Description,
            baseFee:        request.BaseFee,
            freeOverAmount: request.FreeOverAmount,
            isActive:       request.IsActive,
            sortOrder:      request.SortOrder,
            eta:            request.Eta);

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(ToResponse(m)));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var m = await _db.ShippingMethods.FirstOrDefaultAsync(x => x.Id == id);
        if (m == null)
            return NotFound(ApiResponse<object>.Fail("Shipping method not found."));

        // Soft-disable rather than hard delete — historical orders reference the code.
        m.Update(null, null, null, null, isActive: false, null, null);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { Deactivated = id }));
    }

    // -------------------------------------------------------------
    // POST /api/shipping-methods/backfill   — SuperAdmin only
    // Loops every tenant and seeds default shipping methods if they
    // have none. Idempotent — already-configured tenants are skipped.
    // -------------------------------------------------------------
    [HttpPost("backfill")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> Backfill()
    {
        var tenantIds = await _db.Tenants
            .IgnoreQueryFilters()
            .Select(t => t.Id)
            .ToListAsync();

        int seeded = 0, skipped = 0;
        foreach (var tid in tenantIds)
        {
            var hadAny = await _db.ShippingMethods
                .IgnoreQueryFilters()
                .AnyAsync(s => s.TenantId == tid);

            await TenantDefaultsSeeder.SeedAsync(_db, tid);

            if (hadAny) skipped++; else seeded++;
        }

        return Ok(ApiResponse<object>.Ok(new
        {
            TenantsScanned = tenantIds.Count,
            Seeded         = seeded,
            Skipped        = skipped
        }));
    }

    // -------------------------------------------------------------
    // helpers
    // -------------------------------------------------------------
    private static object ToResponse(ShippingMethod m) => new
    {
        m.Id,
        m.Code,
        m.Name,
        m.Description,
        m.BaseFee,
        m.FreeOverAmount,
        m.IsPickup,
        m.IsActive,
        m.SortOrder,
        m.Eta
    };
}

public class CreateShippingMethodRequest
{
    public string   Code           { get; set; } = "";
    public string   Name           { get; set; } = "";
    public string?  Description    { get; set; }
    public decimal  BaseFee        { get; set; }
    public decimal? FreeOverAmount { get; set; }
    public bool     IsPickup       { get; set; }
    public bool?    IsActive       { get; set; }
    public int?     SortOrder      { get; set; }
    public string?  Eta            { get; set; }
}

public class UpdateShippingMethodRequest
{
    public string?  Name           { get; set; }
    public string?  Description    { get; set; }
    public decimal? BaseFee        { get; set; }
    public decimal? FreeOverAmount { get; set; }
    public bool?    IsActive       { get; set; }
    public int?     SortOrder      { get; set; }
    public string?  Eta            { get; set; }
}
