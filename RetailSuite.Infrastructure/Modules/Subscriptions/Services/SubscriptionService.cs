using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Subscriptions.Entities;

namespace RetailSuite.Infrastructure.Modules.Subscriptions.Services;

/// <summary>
/// Public surface for managing a tenant's subscription lifecycle:
/// initial creation at signup, plan changes (upgrade / downgrade), and cancellation.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Create the initial subscription for a newly-signed-up tenant.</summary>
    Task<TenantSubscription> CreateInitialSubscriptionAsync(
        Guid tenantId,
        string planCode,
        BillingCycle billingCycle = BillingCycle.Monthly);

    /// <summary>Return the tenant's currently-active subscription (if any).</summary>
    Task<TenantSubscription?> GetActiveAsync(Guid tenantId);

    /// <summary>
    /// Change the tenant's plan. If newPlan is more expensive than current, the change is immediate
    /// and a proration credit/charge is returned. If cheaper, the change is scheduled for the next renewal.
    /// </summary>
    Task<PlanChangeResult> ChangePlanAsync(
        Guid tenantId,
        string newPlanCode,
        BillingCycle newCycle);

    /// <summary>Schedule cancellation at the current period end (soft-cancel).</summary>
    Task CancelAsync(Guid tenantId);

    /// <summary>Undo a pending cancellation. Allowed only while still in the paid period.</summary>
    Task ResumeAsync(Guid tenantId);
}

/// <summary>Outcome of a ChangePlanAsync call.</summary>
public record PlanChangeResult(
    Guid SubscriptionId,
    string FromPlanCode,
    string ToPlanCode,
    bool EffectiveImmediately,
    decimal ProrationCredit,
    decimal ProrationCharge,
    decimal NetDue);

public class SubscriptionService : ISubscriptionService
{
    private readonly RetailDbContext _db;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(RetailDbContext db, ILogger<SubscriptionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TenantSubscription> CreateInitialSubscriptionAsync(
        Guid tenantId,
        string planCode,
        BillingCycle billingCycle = BillingCycle.Monthly)
    {
        var existing = await GetActiveAsync(tenantId);
        if (existing != null)
            throw new BusinessRuleException($"Tenant {tenantId} already has an active subscription.");

        var plan = await _db.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Code == planCode.ToUpperInvariant() && p.IsActive);
        if (plan == null)
            throw new NotFoundException("SubscriptionPlan", planCode);

        DateTime? trialEnd = plan.TrialDays > 0
            ? DateTime.UtcNow.AddDays(plan.TrialDays)
            : null;

        var sub = new TenantSubscription(tenantId, plan, billingCycle, trialEnd);
        _db.TenantSubscriptions.Add(sub);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Subscription created: Tenant={TenantId}, Plan={PlanCode}, Cycle={Cycle}, Status={Status}",
            tenantId, plan.Code, billingCycle, sub.Status);

        return sub;
    }

    public async Task<TenantSubscription?> GetActiveAsync(Guid tenantId)
    {
        return await _db.TenantSubscriptions
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && !s.IsDeleted)
            .Where(s => s.Status == SubscriptionStatus.Trialing
                     || s.Status == SubscriptionStatus.Active
                     || s.Status == SubscriptionStatus.PastDue
                     || s.Status == SubscriptionStatus.GracePeriod)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<PlanChangeResult> ChangePlanAsync(
        Guid tenantId,
        string newPlanCode,
        BillingCycle newCycle)
    {
        var sub = await GetActiveAsync(tenantId)
            ?? throw new BusinessRuleException("No active subscription to change.");

        var currentPlan = await _db.SubscriptionPlans.FirstAsync(p => p.Id == sub.PlanId);
        var newPlan     = await _db.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Code == newPlanCode.ToUpperInvariant() && p.IsActive)
            ?? throw new NotFoundException("SubscriptionPlan", newPlanCode);

        if (newPlan.Id == currentPlan.Id && newCycle == sub.BillingCycle)
            throw new BusinessRuleException("Already on the requested plan and billing cycle.");

        var currentPeriodPrice = currentPlan.PriceFor(sub.BillingCycle);
        var newPeriodPrice     = newPlan.PriceFor(newCycle);

        var upgrading = newPeriodPrice > currentPeriodPrice;

        decimal prorationCredit = 0m;
        decimal prorationCharge = 0m;

        if (upgrading)
        {
            // Immediate switch with proration credit on unused days of current plan.
            var totalDays = TotalDaysInPeriod(sub);
            var unused    = Math.Max(0, sub.DaysRemainingInPeriod);

            if (totalDays > 0)
            {
                prorationCredit = Math.Round(currentPeriodPrice * unused / totalDays, 2);
                prorationCharge = Math.Round(newPeriodPrice    * unused / totalDays, 2);
            }

            sub.ChangePlan(newPlan, newCycle, effectiveImmediately: true);
            await _db.SaveChangesAsync();
        }
        else
        {
            // Downgrade — schedule at next renewal. No proration, no immediate charge.
            sub.ChangePlan(newPlan, newCycle, effectiveImmediately: false);
            await _db.SaveChangesAsync();
        }

        var net = Math.Max(0, prorationCharge - prorationCredit);

        _logger.LogInformation(
            "Plan changed: Tenant={TenantId}, {From} -> {To} ({Cycle}), Immediate={Immediate}, NetDue={Net} {Currency}",
            tenantId, currentPlan.Code, newPlan.Code, newCycle, upgrading, net, newPlan.Currency);

        return new PlanChangeResult(
            SubscriptionId:        sub.Id,
            FromPlanCode:          currentPlan.Code,
            ToPlanCode:            newPlan.Code,
            EffectiveImmediately:  upgrading,
            ProrationCredit:       prorationCredit,
            ProrationCharge:       prorationCharge,
            NetDue:                net);
    }

    public async Task CancelAsync(Guid tenantId)
    {
        var sub = await GetActiveAsync(tenantId)
            ?? throw new BusinessRuleException("No active subscription to cancel.");

        if (sub.CancelAtPeriodEnd)
            throw new BusinessRuleException("Subscription is already scheduled for cancellation.");

        sub.ScheduleCancellation();
        await _db.SaveChangesAsync();

        _logger.LogInformation("Subscription cancellation scheduled: Tenant={TenantId}, EndDate={EndDate}",
            tenantId, sub.EndDate);
    }

    public async Task ResumeAsync(Guid tenantId)
    {
        var sub = await GetActiveAsync(tenantId)
            ?? throw new BusinessRuleException("No subscription to resume.");

        if (!sub.CancelAtPeriodEnd)
            throw new BusinessRuleException("Subscription is not in a cancelling state.");

        sub.Resume();
        await _db.SaveChangesAsync();

        _logger.LogInformation("Subscription cancellation reversed: Tenant={TenantId}", tenantId);
    }

    // ---- helpers -------------------------------------------------------

    private static int TotalDaysInPeriod(TenantSubscription sub) =>
        Math.Max(1, (int)Math.Round((sub.EndDate - sub.StartDate).TotalDays));
}
