using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure.Modules.Subscriptions.Entities;

namespace RetailSuite.Infrastructure.Modules.Subscriptions.Services;

/// <summary>
/// Runtime checks against the tenant's plan limits.
/// Called by controllers/services right before performing the gated action
/// (e.g. add user, add product, create order).
/// </summary>
public interface IEntitlementService
{
    Task<EntitlementResult> CanAddUserAsync(Guid tenantId);
    Task<EntitlementResult> CanAddProductAsync(Guid tenantId);
    Task<EntitlementResult> CanCreateOrderAsync(Guid tenantId);

    /// <summary>True if the tenant's current plan exposes the named feature.</summary>
    Task<bool> HasFeatureAsync(Guid tenantId, PlanFeature feature);
}

public enum PlanFeature
{
    ApiAccess,
    MultiStore,
    AdvancedAnalytics,
    WebhooksEnabled,
    PrioritySupport
}

/// <summary>
/// Result of an entitlement check. <see cref="Allowed"/> is the truthy field;
/// <see cref="Reason"/> is for explanation when blocked, <see cref="LimitReached"/>
/// is the absolute limit and <see cref="CurrentCount"/> is the current value.
/// </summary>
public record EntitlementResult(bool Allowed, string? Reason, int? CurrentCount, int? Limit)
{
    public static EntitlementResult Allow() => new(true, null, null, null);
    public static EntitlementResult Deny(string reason, int currentCount, int limit)
        => new(false, reason, currentCount, limit);
}

public class EntitlementService : IEntitlementService
{
    private readonly RetailDbContext _db;
    private readonly ILogger<EntitlementService> _logger;

    public EntitlementService(RetailDbContext db, ILogger<EntitlementService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<EntitlementResult> CanAddUserAsync(Guid tenantId)
    {
        var plan = await LoadPlanAsync(tenantId);
        if (plan == null) return EntitlementResult.Allow();  // No subscription = grandfathered

        if (!plan.MaxUsers.HasValue) return EntitlementResult.Allow();

        var current = await _db.Users
            .IgnoreQueryFilters()
            .CountAsync(u => u.TenantId == tenantId && !u.IsDeleted);

        return current >= plan.MaxUsers.Value
            ? EntitlementResult.Deny(
                $"Your plan allows up to {plan.MaxUsers} users. Upgrade to add more.",
                current, plan.MaxUsers.Value)
            : EntitlementResult.Allow();
    }

    public async Task<EntitlementResult> CanAddProductAsync(Guid tenantId)
    {
        var plan = await LoadPlanAsync(tenantId);
        if (plan == null) return EntitlementResult.Allow();
        if (!plan.MaxProducts.HasValue) return EntitlementResult.Allow();

        var current = await _db.Products
            .IgnoreQueryFilters()
            .CountAsync(p => p.TenantId == tenantId && !p.IsDeleted);

        return current >= plan.MaxProducts.Value
            ? EntitlementResult.Deny(
                $"Your plan allows up to {plan.MaxProducts} products. Upgrade to add more.",
                current, plan.MaxProducts.Value)
            : EntitlementResult.Allow();
    }

    public async Task<EntitlementResult> CanCreateOrderAsync(Guid tenantId)
    {
        var plan = await LoadPlanAsync(tenantId);
        if (plan == null) return EntitlementResult.Allow();
        if (!plan.MaxOrdersPerMonth.HasValue) return EntitlementResult.Allow();

        var since = DateTime.UtcNow.AddDays(-30);
        var current = await _db.Orders
            .IgnoreQueryFilters()
            .CountAsync(o => o.TenantId == tenantId && !o.IsDeleted && o.CreatedAt >= since);

        return current >= plan.MaxOrdersPerMonth.Value
            ? EntitlementResult.Deny(
                $"Your plan allows up to {plan.MaxOrdersPerMonth} orders per 30-day window. Upgrade for higher limits.",
                current, plan.MaxOrdersPerMonth.Value)
            : EntitlementResult.Allow();
    }

    public async Task<bool> HasFeatureAsync(Guid tenantId, PlanFeature feature)
    {
        var plan = await LoadPlanAsync(tenantId);
        if (plan == null) return true;   // No subscription = grandfathered

        return feature switch
        {
            PlanFeature.ApiAccess         => plan.ApiAccess,
            PlanFeature.MultiStore        => plan.MultiStore,
            PlanFeature.AdvancedAnalytics => plan.AdvancedAnalytics,
            PlanFeature.WebhooksEnabled   => plan.WebhooksEnabled,
            PlanFeature.PrioritySupport   => plan.PrioritySupport,
            _ => false
        };
    }

    private async Task<SubscriptionPlan?> LoadPlanAsync(Guid tenantId)
    {
        var sub = await _db.TenantSubscriptions
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && !s.IsDeleted)
            .Where(s => s.Status == SubscriptionStatus.Trialing
                     || s.Status == SubscriptionStatus.Active
                     || s.Status == SubscriptionStatus.PastDue
                     || s.Status == SubscriptionStatus.GracePeriod)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.PlanId })
            .FirstOrDefaultAsync();

        if (sub == null) return null;

        return await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == sub.PlanId);
    }
}
