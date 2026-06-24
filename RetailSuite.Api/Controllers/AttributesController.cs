using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Api.Authorization;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Modules.Catalog.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Manage the variant-defining attributes (Size, Color, Material…) and their values.
/// Variants on a product reference one value per attribute via VariantAttributeValue.
/// </summary>
[ApiController]
[Route("api/attributes")]
[RequirePermission(Permissions.Attributes)]
public class AttributesController : ControllerBase
{
    private readonly RetailDbContext _db;
    public AttributesController(RetailDbContext db) => _db = db;

    // GET /api/attributes  — attribute list with their values + variant usage count
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var rows = await _db.ProductAttributes
            .OrderBy(a => a.Name)
            .Select(a => new
            {
                a.Id,
                a.Name,
                Values = _db.ProductAttributeValues
                    .Where(v => v.AttributeId == a.Id)
                    .OrderBy(v => v.Value)
                    .Select(v => new
                    {
                        v.Id,
                        v.Value,
                        UsageCount = _db.VariantAttributeValues
                            .Count(va => va.ProductAttributeValueId == v.Id)
                    })
                    .ToList()
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(rows));
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] AttributeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse<object>.Fail("Name is required."));

        // Reject duplicate attribute names within the same tenant.
        if (await _db.ProductAttributes.AnyAsync(a => a.Name == request.Name.Trim()))
            return Conflict(ApiResponse<object>.Fail($"Attribute '{request.Name}' already exists."));

        var a = new ProductAttribute(request.Name);
        _db.ProductAttributes.Add(a);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { a.Id, a.Name }));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AttributeRequest request)
    {
        var a = await _db.ProductAttributes.FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound(ApiResponse<object>.Fail("Attribute not found."));
        a.Rename(request.Name);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { a.Id, a.Name }));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var a = await _db.ProductAttributes.FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound(ApiResponse<object>.Fail("Attribute not found."));

        // Refuse to delete if any variant is still tied to a value of this attribute.
        var inUse = await _db.VariantAttributeValues
            .Join(_db.ProductAttributeValues,
                  va => va.ProductAttributeValueId,
                  pav => pav.Id,
                  (va, pav) => pav.AttributeId)
            .AnyAsync(aid => aid == id);
        if (inUse)
            return BadRequest(ApiResponse<object>.Fail(
                "Cannot delete — variants currently use values from this attribute. Detach them first."));

        // Cascade delete the values (no FK in the model — clean them up manually).
        var values = await _db.ProductAttributeValues.Where(v => v.AttributeId == id).ToListAsync();
        _db.ProductAttributeValues.RemoveRange(values);
        _db.ProductAttributes.Remove(a);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { Deleted = id }));
    }

    // ----- Values -----------------------------------------------------

    [HttpPost("{id:guid}/values")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AddValue(Guid id, [FromBody] ValueRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
            return BadRequest(ApiResponse<object>.Fail("Value is required."));

        var attr = await _db.ProductAttributes.FirstOrDefaultAsync(a => a.Id == id);
        if (attr == null) return NotFound(ApiResponse<object>.Fail("Attribute not found."));

        var trimmed = request.Value.Trim();
        if (await _db.ProductAttributeValues.AnyAsync(v => v.AttributeId == id && v.Value == trimmed))
            return Conflict(ApiResponse<object>.Fail($"Value '{trimmed}' already exists for this attribute."));

        var v = new ProductAttributeValue(id, trimmed);
        _db.ProductAttributeValues.Add(v);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { v.Id, v.Value }));
    }

    [HttpPut("values/{valueId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateValue(Guid valueId, [FromBody] ValueRequest request)
    {
        var v = await _db.ProductAttributeValues.FirstOrDefaultAsync(x => x.Id == valueId);
        if (v == null) return NotFound(ApiResponse<object>.Fail("Value not found."));
        v.SetValue(request.Value);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { v.Id, v.Value }));
    }

    [HttpDelete("values/{valueId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteValue(Guid valueId)
    {
        var v = await _db.ProductAttributeValues.FirstOrDefaultAsync(x => x.Id == valueId);
        if (v == null) return NotFound(ApiResponse<object>.Fail("Value not found."));

        var inUse = await _db.VariantAttributeValues.AnyAsync(va => va.ProductAttributeValueId == valueId);
        if (inUse)
            return BadRequest(ApiResponse<object>.Fail(
                "Cannot delete — variants currently use this value. Detach them first."));

        _db.ProductAttributeValues.Remove(v);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { Deleted = valueId }));
    }
}

public class AttributeRequest
{
    public string Name { get; set; } = string.Empty;
}

public class ValueRequest
{
    public string Value { get; set; } = string.Empty;
}
