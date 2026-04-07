
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailSuite.Infrastructure.Modules.Tenant.Entities;
using RetailSuite.Infrastructure.Modules.Tenant;
using RetailSuite.Infrastructure;

namespace RetailSuite.Api.Controllers;

[ApiController]
[Route("api/tenants")]
public class TenantsController : ControllerBase
{
    private readonly RetailDbContext _db;

    public TenantsController(RetailDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<IActionResult> Create(string name, string subdomain)
    {
        var tenant = new Tenant(name, subdomain);
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();
        return Ok(tenant);
    }

    [HttpGet]
    public IActionResult Get([FromHeader(Name = "X-Tenant-Id")] Guid tenantId)
    {
        return Ok(_db.Tenants.ToList());
    }
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(User.Claims.Select(c => new { c.Type, c.Value }));
    }
}
