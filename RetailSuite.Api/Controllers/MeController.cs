using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Identity.Services;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Authenticated current-user endpoints — independent of admin user management.
/// Any signed-in tenant user can read their own profile + permissions and change
/// their own password.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly ITenantUserService _users;
    private readonly ICurrentUserContext _currentUser;
    private readonly ITenantContext _tenantContext;

    public MeController(
        RetailDbContext db,
        ITenantUserService users,
        ICurrentUserContext currentUser,
        ITenantContext tenantContext)
    {
        _db            = db;
        _users         = users;
        _currentUser   = currentUser;
        _tenantContext = tenantContext;
    }

    /// <summary>Current user info + the permission codes granted to them.</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (userId, tenantId) = RequireIds();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return NotFound(ApiResponse<object>.Fail("User not found."));

        var perms = await _users.GetPermissionsAsync(tenantId, userId);
        var tenantSubdomain = await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.Subdomain)
            .FirstOrDefaultAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            user.Id,
            user.Email,
            user.FullName,
            Role               = user.Role.ToString(),
            user.IsActive,
            user.MustChangePassword,
            Permissions        = perms,
            TenantSubdomain    = tenantSubdomain
        }));
    }

    /// <summary>Change own password — used by the first-login flow and the profile page.</summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var (userId, tenantId) = RequireIds();
        await _users.ChangePasswordAsync(tenantId, userId, request.CurrentPassword ?? "", request.NewPassword ?? "");
        return Ok(ApiResponse<object>.Ok(new { Message = "Password changed." }));
    }

    private (Guid UserId, Guid TenantId) RequireIds()
    {
        var u = _currentUser.UserId;
        var t = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");
        return (u, t);
    }
}

public class ChangePasswordRequest
{
    public string? CurrentPassword { get; set; }
    public string? NewPassword     { get; set; }
}
