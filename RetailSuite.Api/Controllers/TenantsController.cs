
using Microsoft.AspNetCore.Mvc;
using RetailSuite.Modules.Tenant;
using RetailSuite.Modules.Tenant.Entities;

namespace RetailSuite.Api.Controllers;

[ApiController]
[Route("api/tenants")]
public class TenantsController : ControllerBase
{
    private readonly TenantDbContext _db;

    public TenantsController(TenantDbContext db)
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
}
