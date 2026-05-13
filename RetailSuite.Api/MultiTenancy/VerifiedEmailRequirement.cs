using Microsoft.AspNetCore.Authorization;

namespace RetailSuite.Api.MultiTenancy;

/// <summary>
/// Authorization requirement enforcing that the calling user has verified their email.
/// Apply via the "RequireVerifiedEmail" policy.
/// </summary>
public class VerifiedEmailRequirement : IAuthorizationRequirement { }

public class VerifiedEmailHandler : AuthorizationHandler<VerifiedEmailRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        VerifiedEmailRequirement requirement)
    {
        // SuperAdmin bypass — they manage tenants and don't have a tenant email to verify.
        if (context.User.IsInRole("SuperAdmin"))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var verifiedClaim = context.User.FindFirst("email_verified")?.Value;
        if (string.Equals(verifiedClaim, "true", StringComparison.OrdinalIgnoreCase))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
