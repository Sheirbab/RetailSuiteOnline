using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Customer.Model;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Infrastructure.Modules.Tenant.Entities;
using RetailSuite.Modules.Accounting.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly ITenantContext  _tenantContext;

    public TenantsController(RetailDbContext db, ITenantContext tenantContext)
    {
        _db            = db;
        _tenantContext = tenantContext;
    }

    // ---------------------------------------------------------------
    // GET /api/tenants/me  — any authenticated user
    // ---------------------------------------------------------------
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var tenantId = _tenantContext.TenantId;

        if (!tenantId.HasValue)
            return Unauthorized(ApiResponse<object>.Fail("Tenant context not available."));

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId.Value);

        if (tenant == null)
            return NotFound(ApiResponse<object>.Fail("Tenant not found."));

        return Ok(ApiResponse<object>.Ok(new
        {
            tenant.Id,
            tenant.Name,
            tenant.Subdomain,
            tenant.Status,
            tenant.CreatedAt
        }));
    }

    // ---------------------------------------------------------------
    // GET /api/tenants  — SuperAdmin: list all tenants with user count
    // ---------------------------------------------------------------
    [HttpGet]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> GetAll()
    {
        var tenants = await _db.Tenants
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        // Count users per tenant (ignore global filter — super admin sees all)
        var userCounts = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId != Guid.Empty)
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count);

        var result = tenants.Select(t => new
        {
            t.Id,
            t.Name,
            t.Subdomain,
            t.Status,
            t.CreatedAt,
            UserCount = userCounts.TryGetValue(t.Id, out var c) ? c : 0
        });

        return Ok(ApiResponse<object>.Ok(result));
    }

    // ---------------------------------------------------------------
    // POST /api/tenants  — SuperAdmin: create a new tenant + admin user
    // ---------------------------------------------------------------
    [HttpPost]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TenantName) ||
            string.IsNullOrWhiteSpace(request.Subdomain)  ||
            string.IsNullOrWhiteSpace(request.AdminEmail))
            return BadRequest(ApiResponse<object>.Fail("TenantName, Subdomain and AdminEmail are required."));

        if (await _db.Tenants.AnyAsync(t => t.Subdomain == request.Subdomain))
            return Conflict(ApiResponse<object>.Fail("Subdomain is already taken."));

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // 1. Create Tenant
            var tenant = new Tenant(request.TenantName.Trim(), request.Subdomain.Trim().ToLowerInvariant());
            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync();

            // 2. Generate temp password and create Admin user
            var tempPassword = GenerateTempPassword();
            var hash         = BCrypt.Net.BCrypt.HashPassword(tempPassword);
            var adminUser    = new User(tenant.Id, request.AdminEmail.Trim().ToLowerInvariant(), hash, UserRole.Admin);
            _db.Users.Add(adminUser);
            await _db.SaveChangesAsync();

            // 3. Seed Chart of Accounts
            var accounts = new List<Account>
            {
                new("1000", "Cash",                AccountType.Asset)     { TenantId = tenant.Id },
                new("1100", "Inventory",           AccountType.Asset)     { TenantId = tenant.Id },
                new("1200", "Accounts Receivable", AccountType.Asset)     { TenantId = tenant.Id },
                new("2000", "Tax Payable",         AccountType.Liability) { TenantId = tenant.Id },
                new("4000", "Revenue",             AccountType.Revenue)   { TenantId = tenant.Id },
                new("5000", "Cost of Goods Sold",  AccountType.Expense)   { TenantId = tenant.Id },
            };
            _db.Accounts.AddRange(accounts);
            await _db.SaveChangesAsync();

            await tx.CommitAsync();

            return Ok(ApiResponse<object>.Ok(new
            {
                TenantId     = tenant.Id,
                TenantName   = tenant.Name,
                Subdomain    = tenant.Subdomain,
                AdminEmail   = adminUser.Email,
                TempPassword = tempPassword
            }));
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ---------------------------------------------------------------
    // PATCH /api/tenants/{id}/status  — SuperAdmin: activate / deactivate
    // ---------------------------------------------------------------
    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetTenantStatusRequest request)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
        if (tenant == null)
            return NotFound(ApiResponse<object>.Fail("Tenant not found."));

        if (request.Status != "Active" && request.Status != "Inactive")
            return BadRequest(ApiResponse<object>.Fail("Status must be 'Active' or 'Inactive'."));

        tenant.SetStatus(request.Status);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new { tenant.Id, tenant.Status }));
    }

    // ---------------------------------------------------------------
    // GET /api/tenants/{id}/users  — SuperAdmin: list users of a tenant
    // ---------------------------------------------------------------
    [HttpGet("{id:guid}/users")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> GetUsers(Guid id)
    {
        var tenantExists = await _db.Tenants.AnyAsync(t => t.Id == id);
        if (!tenantExists)
            return NotFound(ApiResponse<object>.Fail("Tenant not found."));

        var users = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == id && !u.IsDeleted)
            .OrderBy(u => u.Email)
            .Select(u => new { u.Id, u.Email, Role = u.Role.ToString(), u.CreatedAt })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(users));
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------
    private static string GenerateTempPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$";
        var rng    = new Random(Guid.NewGuid().GetHashCode());
        var result = new char[12];
        for (int i = 0; i < result.Length; i++)
            result[i] = chars[rng.Next(chars.Length)];
        return new string(result);
    }
}

// ---------------------------------------------------------------
// Request DTOs
// ---------------------------------------------------------------
public class CreateTenantRequest
{
    public string TenantName  { get; set; } = string.Empty;
    public string Subdomain   { get; set; } = string.Empty;
    public string AdminEmail  { get; set; } = string.Empty;
}

public class SetTenantStatusRequest
{
    public string Status { get; set; } = "Active";
}
