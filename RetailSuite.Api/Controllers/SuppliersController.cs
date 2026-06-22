using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Suppliers.Dtos;
using RetailSuite.Infrastructure.Modules.Suppliers.Entities;
using RetailSuite.Shared;
using RetailSuite.Api.Authorization;
using RetailSuite.Infrastructure.Modules.Identity.Entities;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Vendor / wholesaler CRUD. Suppliers are referenced by ReceivingOrders.
/// </summary>
[ApiController]
[Route("api/suppliers")]
[RequirePermission(Permissions.Suppliers)]
public class SuppliersController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly ITenantContext _tenantContext;

    public SuppliersController(RetailDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    // -------------------------------------------------------------
    // GET /api/suppliers?active=true
    // -------------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? active)
    {
        var q = _db.Suppliers.AsQueryable();
        if (active.HasValue) q = q.Where(s => s.IsActive == active.Value);

        var rows = await q
            .OrderBy(s => s.Name)
            .Select(s => s.ToResponse())
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(rows));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var supplier = await _db.Suppliers
            .Where(s => s.Id == id)
            .Select(s => s.ToResponse())
            .FirstOrDefaultAsync();
        if (supplier == null)
            return NotFound(ApiResponse<object>.Fail("Supplier not found."));
        return Ok(ApiResponse<object>.Ok(supplier));
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateSupplierRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse<object>.Fail("Name is required."));

        var tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");

        var supplier = new Supplier(tenantId, request.Name);
        supplier.UpdateContact(request.ContactPerson, request.Phone, request.Email, request.Address);
        supplier.SetNotes(request.Notes);

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(supplier.ToResponse()));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSupplierRequest request)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id);
        if (supplier == null)
            return NotFound(ApiResponse<object>.Fail("Supplier not found."));

        if (!string.IsNullOrWhiteSpace(request.Name)) supplier.Rename(request.Name);

        if (request.ContactPerson != null || request.Phone != null
            || request.Email != null || request.Address != null)
        {
            supplier.UpdateContact(
                request.ContactPerson ?? supplier.ContactPerson,
                request.Phone         ?? supplier.Phone,
                request.Email         ?? supplier.Email,
                request.Address       ?? supplier.Address);
        }

        if (request.Notes != null) supplier.SetNotes(request.Notes);

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value) supplier.Activate();
            else supplier.Deactivate();
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(supplier.ToResponse()));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id);
        if (supplier == null)
            return NotFound(ApiResponse<object>.Fail("Supplier not found."));

        // Block hard-delete if there are referencing receiving orders — deactivate instead.
        var inUse = await _db.ReceivingOrders.AnyAsync(o => o.SupplierId == id);
        if (inUse)
        {
            supplier.Deactivate();
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(new { Deactivated = id, Reason = "In use by receiving orders." }));
        }

        supplier.MarkAsDeleted();
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { Deleted = id }));
    }
}
