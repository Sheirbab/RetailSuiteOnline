using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Locations.Entities;
using RetailSuite.Infrastructure.Modules.Locations.Services;
using RetailSuite.Shared;
using RetailSuite.Api.Authorization;
using RetailSuite.Infrastructure.Modules.Identity.Entities;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Branches / shops where stock is held and sales happen. Each tenant has
/// exactly one default location (used for online orders and as the POS default).
/// </summary>
[ApiController]
[Route("api/locations")]
[RequirePermission(Permissions.Locations)]
public class LocationsController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly ILocationService _service;

    public LocationsController(RetailDbContext db, ILocationService service)
    {
        _db      = db;
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? active)
    {
        var q = _db.Locations.AsQueryable();
        if (active.HasValue) q = q.Where(l => l.IsActive == active.Value);

        var rows = await q
            .OrderByDescending(l => l.IsDefault)
            .ThenBy(l => l.Name)
            .Select(l => ToResponse(l))
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(rows));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var loc = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (loc == null)
            return NotFound(ApiResponse<object>.Fail("Location not found."));
        return Ok(ApiResponse<object>.Ok(ToResponse(loc)));
    }

    [HttpGet("default")]
    public async Task<IActionResult> Default()
    {
        var loc = await _service.GetDefaultAsync();
        if (loc == null)
            return NotFound(ApiResponse<object>.Fail("No default location set."));
        return Ok(ApiResponse<object>.Ok(ToResponse(loc)));
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateLocationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse<object>.Fail("Code and Name are required."));

        var loc = await _service.CreateAsync(
            request.Code, request.Name, request.Address, request.Phone, request.Notes,
            makeDefault: request.MakeDefault);

        return Ok(ApiResponse<object>.Ok(ToResponse(loc)));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLocationRequest request)
    {
        var loc = await _service.UpdateAsync(
            id, request.Name, request.Address, request.Phone, request.Notes, request.IsActive);
        return Ok(ApiResponse<object>.Ok(ToResponse(loc)));
    }

    [HttpPost("{id:guid}/set-default")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> SetDefault(Guid id)
    {
        await _service.SetDefaultAsync(id);
        return Ok(ApiResponse<object>.Ok(new { Default = id }));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var loc = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (loc == null)
            return NotFound(ApiResponse<object>.Fail("Location not found."));
        if (loc.IsDefault)
            return BadRequest(ApiResponse<object>.Fail("Cannot delete the default location."));

        // Soft-disable rather than hard delete — history references this location.
        loc.Deactivate();
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { Deactivated = id }));
    }

    // ----- helpers ------------------------------------------------------

    private static object ToResponse(Location l) => new
    {
        l.Id,
        l.Code,
        l.Name,
        l.Address,
        l.Phone,
        l.Notes,
        l.IsActive,
        l.IsDefault
    };
}

public class CreateLocationRequest
{
    public string  Code        { get; set; } = "";
    public string  Name        { get; set; } = "";
    public string? Address     { get; set; }
    public string? Phone       { get; set; }
    public string? Notes       { get; set; }
    public bool    MakeDefault { get; set; }
}

public class UpdateLocationRequest
{
    public string? Name     { get; set; }
    public string? Address  { get; set; }
    public string? Phone    { get; set; }
    public string? Notes    { get; set; }
    public bool?   IsActive { get; set; }
}
