using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;

namespace RetailSuite.Api.Authorization;

/// <summary>
/// Authorization handler for <see cref="RequirePermissionRequirement"/>.
/// Pass conditions:
///   1. Role claim is "SuperAdmin" or "Admin" → bypass (admins have everything).
///   2. The user has a <c>UserPermission</c> row matching the required code.
/// </summary>
public class RequirePermissionHandler : AuthorizationHandler<RequirePermissionRequirement>
{
    private readonly IServiceScopeFactory _scopes;

    public RequirePermissionHandler(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, RequirePermissionRequirement requirement)
    {
        // 1. Admin bypass.
        var role = context.User.FindFirst(ClaimTypes.Role)?.Value
                 ?? context.User.FindFirst("role")?.Value;
        if (string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase)
         || string.Equals(role, "Admin",      StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
            return;
        }

        // 2. Look up an explicit grant.
        var userIdRaw = context.User.FindFirst("userId")?.Value;
        if (!Guid.TryParse(userIdRaw, out var userId))
            return;   // anonymous or malformed token — fail

        // RetailDbContext is scoped; create a scope so the handler (singleton-friendly) can resolve it.
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();

        var hasIt = await db.UserPermissions
            .IgnoreQueryFilters()
            .AnyAsync(p => p.UserId == userId && p.Code == requirement.Code);

        if (hasIt) context.Succeed(requirement);
    }
}
