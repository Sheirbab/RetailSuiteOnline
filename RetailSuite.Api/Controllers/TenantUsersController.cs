using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailSuite.Infrastructure.Modules.Customer.Model;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Infrastructure.Modules.Identity.Services;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Tenant admin user management — list / create / update staff users,
/// reset passwords, and manage per-user permissions.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = "AdminOnly")]
public class TenantUsersController : ControllerBase
{
    private readonly ITenantUserService _service;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUser;

    public TenantUsersController(
        ITenantUserService service,
        ITenantContext tenantContext,
        ICurrentUserContext currentUser)
    {
        _service       = service;
        _tenantContext = tenantContext;
        _currentUser   = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var tenantId = RequireTenantId();
        var users = await _service.ListAsync(tenantId);
        return Ok(ApiResponse<object>.Ok(users.Select(u => new
        {
            u.Id,
            u.Email,
            u.FullName,
            Role             = u.Role.ToString(),
            u.IsActive,
            u.IsEmailVerified,
            u.MustChangePassword,
            u.CreatedAt
        })));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var tenantId = RequireTenantId();
        var user = await _service.GetAsync(tenantId, id);
        if (user == null)
            return NotFound(ApiResponse<object>.Fail("User not found."));

        var perms = await _service.GetPermissionsAsync(tenantId, id);
        return Ok(ApiResponse<object>.Ok(new
        {
            user.Id,
            user.Email,
            user.FullName,
            Role             = user.Role.ToString(),
            user.IsActive,
            user.MustChangePassword,
            Permissions      = perms
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTenantUserRequest request)
    {
        var tenantId = RequireTenantId();
        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
            return BadRequest(ApiResponse<object>.Fail("Invalid role."));

        var result = await _service.CreateAsync(tenantId, request.Email, request.FullName, role);

        // If the admin provided initial permissions, apply them.
        if (request.Permissions != null && request.Permissions.Any())
            await _service.SetPermissionsAsync(tenantId, result.UserId, request.Permissions);

        return Ok(ApiResponse<object>.Ok(new
        {
            result.UserId,
            result.Email,
            TempPassword = result.TempPassword,
            Message      = "User created. Share the temp password with them; they'll be prompted to change it on first login."
        }));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenantUserRequest request)
    {
        var tenantId = RequireTenantId();
        UserRole? role = null;
        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var parsed))
                return BadRequest(ApiResponse<object>.Fail("Invalid role."));
            role = parsed;
        }
        await _service.UpdateAsync(tenantId, id, request.FullName, role, request.IsActive);
        return Ok(ApiResponse<object>.Ok(new { Updated = id }));
    }

    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id)
    {
        var tenantId = RequireTenantId();
        var tempPassword = await _service.ResetPasswordAsync(tenantId, id);
        return Ok(ApiResponse<object>.Ok(new
        {
            TempPassword = tempPassword,
            Message      = "Share this with the user. They'll be prompted to change it on next login."
        }));
    }

    [HttpGet("{id:guid}/permissions")]
    public async Task<IActionResult> GetPermissions(Guid id)
    {
        var tenantId = RequireTenantId();
        var perms = await _service.GetPermissionsAsync(tenantId, id);
        return Ok(ApiResponse<object>.Ok(perms));
    }

    [HttpPut("{id:guid}/permissions")]
    public async Task<IActionResult> SetPermissions(Guid id, [FromBody] SetPermissionsRequest request)
    {
        var tenantId = RequireTenantId();
        await _service.SetPermissionsAsync(tenantId, id, request.Codes ?? new List<string>());
        return Ok(ApiResponse<object>.Ok(new { Set = id }));
    }

    /// <summary>Permission catalogue — for the admin UI to render the grid.</summary>
    [HttpGet("/api/permissions/catalog")]
    [AllowAnonymous]   // safe — just the list of available codes, no tenant data
    public IActionResult Catalog()
    {
        return Ok(ApiResponse<object>.Ok(Permissions.Catalog.Select(g => new
        {
            g.Title,
            Entries = g.Entries.Select(e => new { e.Code, e.Label })
        })));
    }

    // ----- helpers -----------------------------------------------------------

    private Guid RequireTenantId() =>
        _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");
}

public class CreateTenantUserRequest
{
    public string  Email       { get; set; } = "";
    public string? FullName    { get; set; }
    public string  Role        { get; set; } = "Staff";
    public List<string>? Permissions { get; set; }
}

public class UpdateTenantUserRequest
{
    public string? FullName { get; set; }
    public string? Role     { get; set; }
    public bool?   IsActive { get; set; }
}

public class SetPermissionsRequest
{
    public List<string>? Codes { get; set; }
}
