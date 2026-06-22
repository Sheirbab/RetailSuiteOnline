using Microsoft.AspNetCore.Authorization;

namespace RetailSuite.Api.Authorization;

/// <summary>
/// Server-side enforcement of a permission code. Applies as an ASP.NET Core
/// authorization policy named <c>perm:{Code}</c>, resolved on-demand by
/// <see cref="PermissionPolicyProvider"/>.
///
/// Usage:
///   [RequirePermission(Permissions.Products)]         on controller or action
///
/// Admins and SuperAdmins always pass — see <see cref="RequirePermissionHandler"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public string Code { get; }

    public RequirePermissionAttribute(string code)
    {
        Code   = code;
        Policy = PolicyPrefix + code;
    }
}

/// <summary>The authorization-policy requirement carrying the permission code.</summary>
public class RequirePermissionRequirement : IAuthorizationRequirement
{
    public string Code { get; }
    public RequirePermissionRequirement(string code) => Code = code;
}
