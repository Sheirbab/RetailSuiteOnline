using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Subscriptions.Entities;
using RetailSuite.Infrastructure.Modules.Subscriptions.Services;
using RetailSuite.Shared;

namespace RetailSuite.Tests.Unit;

/// <summary>
/// Verifies SubscriptionService lifecycle behaviour:
/// initial create with trial, plan change with proration on upgrade,
/// scheduled-cancel + resume, and "downgrade defers to next period" rule.
/// </summary>
public class SubscriptionServiceTests
{
    private static RetailDbContext NewDb()
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<RetailDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RetailDbContext(options, tenantContext.Object);
    }

    private static SubscriptionService NewService(RetailDbContext db) =>
        new(db, NullLogger<SubscriptionService>.Instance);

    private static SubscriptionPlan SeedPlan(
        RetailDbContext db,
        string code,
        decimal monthlyPrice,
        decimal yearlyPrice,
        int trialDays = 0)
    {
        var plan = new SubscriptionPlan(
            code, code + " plan", $"{code} description",
            monthlyPrice, yearlyPrice, trialDays);
        db.SubscriptionPlans.Add(plan);
        db.SaveChanges();
        return plan;
    }

    [Fact]
    public async Task CreateInitialSubscriptionAsync_StartsInTrialing_WhenPlanHasTrialDays()
    {
        await using var db = NewDb();
        SeedPlan(db, "STARTER", 2_500m, 25_000m, trialDays: 14);

        var service = NewService(db);
        var tenantId = Guid.NewGuid();

        var sub = await service.CreateInitialSubscriptionAsync(tenantId, "STARTER");

        Assert.Equal(SubscriptionStatus.Trialing, sub.Status);
        Assert.NotNull(sub.TrialEndsAt);
        Assert.True(sub.TrialEndsAt > DateTime.UtcNow);
        Assert.True(sub.IsInTrial);
    }

    [Fact]
    public async Task CreateInitialSubscriptionAsync_StartsActive_WhenPlanHasNoTrial()
    {
        await using var db = NewDb();
        SeedPlan(db, "FREE", 0m, 0m, trialDays: 0);

        var service = NewService(db);
        var tenantId = Guid.NewGuid();

        var sub = await service.CreateInitialSubscriptionAsync(tenantId, "FREE");

        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        Assert.Null(sub.TrialEndsAt);
    }

    [Fact]
    public async Task CreateInitialSubscriptionAsync_PlanCodeIsCaseInsensitive()
    {
        await using var db = NewDb();
        SeedPlan(db, "PRO", 7_500m, 75_000m);

        var service = NewService(db);
        var tenantId = Guid.NewGuid();

        var sub = await service.CreateInitialSubscriptionAsync(tenantId, "pro");
        Assert.Equal("PRO", sub.PlanCode);
    }

    [Fact]
    public async Task CreateInitialSubscriptionAsync_ThrowsIfAlreadySubscribed()
    {
        await using var db = NewDb();
        SeedPlan(db, "FREE", 0m, 0m);

        var service = NewService(db);
        var tenantId = Guid.NewGuid();
        await service.CreateInitialSubscriptionAsync(tenantId, "FREE");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CreateInitialSubscriptionAsync(tenantId, "FREE"));
    }

    [Fact]
    public async Task CreateInitialSubscriptionAsync_ThrowsForUnknownPlan()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CreateInitialSubscriptionAsync(Guid.NewGuid(), "DOES_NOT_EXIST"));
    }

    [Fact]
    public async Task ChangePlanAsync_Upgrade_IsImmediateAndComputesProration()
    {
        await using var db = NewDb();
        SeedPlan(db, "STARTER", monthlyPrice: 2_500m, yearlyPrice: 25_000m);
        SeedPlan(db, "PRO",     monthlyPrice: 7_500m, yearlyPrice: 75_000m);

        var service = NewService(db);
        var tenantId = Guid.NewGuid();
        await service.CreateInitialSubscriptionAsync(tenantId, "STARTER");

        var result = await service.ChangePlanAsync(tenantId, "PRO", BillingCycle.Monthly);

        Assert.True(result.EffectiveImmediately);
        Assert.Equal("STARTER", result.FromPlanCode);
        Assert.Equal("PRO", result.ToPlanCode);

        // Net due = prorationCharge - prorationCredit. Both should be >= 0 and within plan prices.
        Assert.True(result.ProrationCharge >= 0);
        Assert.True(result.ProrationCredit >= 0);
        Assert.True(result.NetDue >= 0);
        Assert.True(result.ProrationCharge <= 7_500m);
        Assert.True(result.ProrationCredit <= 2_500m);
    }

    [Fact]
    public async Task ChangePlanAsync_Downgrade_IsDeferredAndHasZeroProration()
    {
        await using var db = NewDb();
        SeedPlan(db, "PRO",     monthlyPrice: 7_500m, yearlyPrice: 75_000m);
        SeedPlan(db, "STARTER", monthlyPrice: 2_500m, yearlyPrice: 25_000m);

        var service = NewService(db);
        var tenantId = Guid.NewGuid();
        await service.CreateInitialSubscriptionAsync(tenantId, "PRO");

        var result = await service.ChangePlanAsync(tenantId, "STARTER", BillingCycle.Monthly);

        Assert.False(result.EffectiveImmediately);
        Assert.Equal(0m, result.ProrationCharge);
        Assert.Equal(0m, result.ProrationCredit);
        Assert.Equal(0m, result.NetDue);
    }

    [Fact]
    public async Task ChangePlanAsync_RejectsSamePlanAndCycle()
    {
        await using var db = NewDb();
        SeedPlan(db, "FREE", 0m, 0m);

        var service = NewService(db);
        var tenantId = Guid.NewGuid();
        await service.CreateInitialSubscriptionAsync(tenantId, "FREE");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ChangePlanAsync(tenantId, "FREE", BillingCycle.Monthly));
    }

    [Fact]
    public async Task CancelAsync_SetsCancelAtPeriodEnd()
    {
        await using var db = NewDb();
        SeedPlan(db, "STARTER", 2_500m, 25_000m);

        var service = NewService(db);
        var tenantId = Guid.NewGuid();
        await service.CreateInitialSubscriptionAsync(tenantId, "STARTER");

        await service.CancelAsync(tenantId);

        var sub = await service.GetActiveAsync(tenantId);
        Assert.NotNull(sub);
        Assert.True(sub!.CancelAtPeriodEnd);
        Assert.NotNull(sub.CancelledAt);
        // Status still active during the paid window.
        Assert.True(sub.IsActive);
    }

    [Fact]
    public async Task CancelThenResume_UndoesCancellation()
    {
        await using var db = NewDb();
        SeedPlan(db, "STARTER", 2_500m, 25_000m);

        var service = NewService(db);
        var tenantId = Guid.NewGuid();
        await service.CreateInitialSubscriptionAsync(tenantId, "STARTER");

        await service.CancelAsync(tenantId);
        await service.ResumeAsync(tenantId);

        var sub = await service.GetActiveAsync(tenantId);
        Assert.False(sub!.CancelAtPeriodEnd);
        Assert.Null(sub.CancelledAt);
    }
}
