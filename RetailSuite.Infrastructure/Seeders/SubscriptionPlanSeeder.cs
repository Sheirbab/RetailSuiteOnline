using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure.Modules.Subscriptions.Entities;

namespace RetailSuite.Infrastructure.Seeders;

/// <summary>
/// Seeds the default subscription plans on first run.
/// Idempotent — only inserts plans whose Code does not already exist, so
/// SuperAdmin edits via the API are never overwritten by a restart.
/// </summary>
public static class SubscriptionPlanSeeder
{
    public static async Task SeedAsync(RetailDbContext db, ILogger? logger = null)
    {
        var defaults = BuildDefaults();

        foreach (var plan in defaults)
        {
            var exists = await db.SubscriptionPlans
                .AsNoTracking()
                .AnyAsync(p => p.Code == plan.Code);

            if (exists) continue;

            db.SubscriptionPlans.Add(plan);
            logger?.LogInformation("Seeded subscription plan: {Code}", plan.Code);
        }

        await db.SaveChangesAsync();
    }

    private static List<SubscriptionPlan> BuildDefaults()
    {
        // FREE - hobby / trial floor
        var free = new SubscriptionPlan(
            code: "FREE",
            name: "Free",
            description: "For trying RetailSuite out. Includes basic catalog and order tracking.",
            monthlyPrice: 0m,
            yearlyPrice: 0m,
            trialDays: 0);
        free.UpdateLimits(maxUsers: 1, maxProducts: 50, maxOrdersPerMonth: 100, maxStorageMb: 100);
        free.UpdateFeatures(apiAccess: false, multiStore: false, advancedAnalytics: false, webhooksEnabled: false, prioritySupport: false);
        free.SetSortOrder(10);

        // STARTER - single-shop retailer
        var starter = new SubscriptionPlan(
            code: "STARTER",
            name: "Starter",
            description: "For a single shop. Email support, basic reports, multi-user access.",
            monthlyPrice: 2_500m,
            yearlyPrice: 25_000m,  // ~17% annual discount
            trialDays: 14);
        starter.UpdateLimits(maxUsers: 3, maxProducts: 500, maxOrdersPerMonth: 1_000, maxStorageMb: 1_024);
        starter.UpdateFeatures(apiAccess: false, multiStore: false, advancedAnalytics: false, webhooksEnabled: false, prioritySupport: false);
        starter.SetSortOrder(20);

        // PRO - growing chain
        var pro = new SubscriptionPlan(
            code: "PRO",
            name: "Pro",
            description: "For growing retailers. API access, multi-store, advanced analytics and webhooks.",
            monthlyPrice: 7_500m,
            yearlyPrice: 75_000m,
            trialDays: 14);
        pro.UpdateLimits(maxUsers: 10, maxProducts: null, maxOrdersPerMonth: null, maxStorageMb: 10_240);
        pro.UpdateFeatures(apiAccess: true, multiStore: true, advancedAnalytics: true, webhooksEnabled: true, prioritySupport: false);
        pro.SetSortOrder(30);

        // ENTERPRISE - custom contract; "Contact us" pricing
        var enterprise = new SubscriptionPlan(
            code: "ENTERPRISE",
            name: "Enterprise",
            description: "Custom contract — dedicated support, on-prem option, SLA. Contact sales.",
            monthlyPrice: 0m,   // priced via custom contract
            yearlyPrice: 0m,
            trialDays: 14);
        enterprise.UpdateLimits(maxUsers: null, maxProducts: null, maxOrdersPerMonth: null, maxStorageMb: null);
        enterprise.UpdateFeatures(apiAccess: true, multiStore: true, advancedAnalytics: true, webhooksEnabled: true, prioritySupport: true);
        enterprise.SetSortOrder(40);

        return new() { free, starter, pro, enterprise };
    }
}
