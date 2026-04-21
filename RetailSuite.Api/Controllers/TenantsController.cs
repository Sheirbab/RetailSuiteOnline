using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly ITenantContext _tenantContext;

    public TenantsController(RetailDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Returns the current authenticated user's tenant details.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var tenantId = _tenantContext.TenantId;

        if (!tenantId.HasValue)
            return Unauthorized(ApiResponse<object>.Fail("Tenant context not available."));

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId.Value);

        if (tenant == null)
            return NotFound(ApiResponse<object>.Fail("Tenant not found."));

        return Ok(new ApiResponse<object>(true, null, new
        {
            tenant.Id,
            tenant.Name,
            tenant.Subdomain,
            tenant.Status,
            tenant.CreatedAt
        }));
    }
}
