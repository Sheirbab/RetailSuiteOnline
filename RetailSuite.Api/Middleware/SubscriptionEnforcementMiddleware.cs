using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Subscriptions.Entities;
using RetailSuite.Infrastructure.Modules.Tenant.Entities;
using RetailSuite.Shared;
using System.Security.Claims;
using System.Text.Json;

namespace RetailSuite.Api.Middleware;

/// <summary>
/// Blocks tenant requests when the tenant or its subscription is in a non-paying state.
/// Returns HTTP 402 (Payment Required) for Suspended tenants and expired subscriptions.
/// </summary>
/// <remarks>
/// Runs AFTER auth/authorization so we have <c>tenantId</c> in claims.
/// Allowlisted paths (auth, webhooks, subscription self-service, swagger, health) pass through
/// so a Suspended tenant can still pay, verify, or read their billing status.
/// </remarks>
public class SubscriptionEnforcementMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SubscriptionEnforcementMiddleware> _logger;

    /// <summary>
    /// Paths that must remain reachable regardless of subscription status.
    /// Matched as case-insensitive starts-with against the request path.
    /// </summary>
    private static readonly string[] Allowlist = new[]
    {
        "/",
        "/health",
        "/swagger",
        "/api/auth",
        "/api/webhooks",
        "/api/subscriptions",
        "/api/billing",          // Suspended tenants must still be able to settle invoices.
        "/api/tenants/me"
    };

    public SubscriptionEnforcementMiddleware(
        RequestDelegate next,
        ILogger<SubscriptionEnforcementMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RetailDbContext db)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (IsAllowlisted(path))
        {
            await _next(context);
            return;
        }

        var tenantIdClaim = context.User.FindFirst("tenantId")?.Value;

        // Unauthenticated or SuperAdmin requests skip subscription gating.
        if (string.IsNullOrEmpty(tenantIdClaim) || context.User.IsInRole("SuperAdmin"))
        {
            await _next(context);
            return;
        }

        if (!Guid.TryParse(tenantIdClaim, out var tenantId) || tenantId == Guid.Empty)
        {
            await _next(context);
            return;
        }

        var status = await db.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Status)
            .FirstOrDefaultAsync();

        if (status == TenantStatus.Suspended || status == TenantStatus.Cancelled)
        {
            await WritePaymentRequiredAsync(context, "Your tenant is suspended. Please settle outstanding charges to restore access.");
            _logger.LogWarning(
                "Request blocked — tenant suspended/cancelled: Tenant={TenantId}, Path={Path}, Status={Status}",
                tenantId, path, status);
            return;
        }

        // Check subscription state — Expired or hard-Cancelled subs block access.
        var sub = await db.TenantSubscriptions
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && !s.IsDeleted)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.Status, s.EndDate })
            .FirstOrDefaultAsync();

        if (sub != null)
        {
            if (sub.Status == SubscriptionStatus.Expired ||
                sub.Status == SubscriptionStatus.Cancelled)
            {
                await WritePaymentRequiredAsync(context, "Your subscription has ended. Please choose a plan to continue.");
                _logger.LogWarning(
                    "Request blocked — subscription not active: Tenant={TenantId}, Path={Path}, SubStatus={SubStatus}",
                    tenantId, path, sub.Status);
                return;
            }

            // Treat soft-cancelled subs whose end date has passed as Expired.
            if (sub.Status == SubscriptionStatus.Active && sub.EndDate <= DateTime.UtcNow)
            {
                await WritePaymentRequiredAsync(context, "Your subscription period has ended. Please renew to continue.");
                return;
            }
        }

        await _next(context);
    }

    private static bool IsAllowlisted(string path)
    {
        foreach (var allowed in Allowlist)
        {
            if (path.Equals(allowed, StringComparison.OrdinalIgnoreCase))
                return true;

            if (allowed.Length > 1 && path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static async Task WritePaymentRequiredAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
        context.Response.ContentType = "application/json";

        var payload = new ApiResponse<string>(false, message, null);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
