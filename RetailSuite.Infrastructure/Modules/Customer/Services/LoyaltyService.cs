using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Customer.Entities;

namespace RetailSuite.Infrastructure.Modules.Customer.Services;

/// <summary>
/// Outcome of a loyalty redemption — returns how many rupees the customer's points
/// translated into and the resulting ledger entry id.
/// </summary>
public record LoyaltyRedeemResult(int PointsRedeemed, decimal RupeesValue, Guid TransactionId);

/// <summary>
/// Per-tenant configurable loyalty:
/// earn N points on every Rs M spent (configurable), redeem points at Rs Y / point.
/// </summary>
public interface ILoyaltyService
{
    /// <summary>Loyalty config for the tenant — auto-creates with sane defaults on first use.</summary>
    Task<LoyaltySettings> GetSettingsAsync(Guid tenantId);

    /// <summary>Update the tenant's loyalty config. Admin endpoint.</summary>
    Task<LoyaltySettings> UpdateSettingsAsync(
        Guid tenantId,
        decimal? rupeesPerPoint,
        int? minRedeemPoints,
        decimal? pointValueRupees,
        decimal? maxRedemptionPercentOfOrder,
        bool? isEnabled);

    /// <summary>Current point balance for a customer.</summary>
    Task<int> GetBalanceAsync(Guid tenantId, Guid customerId);

    /// <summary>
    /// Auto-earn for a completed order. Computes points from the order total using the
    /// tenant's <see cref="LoyaltySettings.RupeesPerPoint"/>. No-op if customer is null /
    /// loyalty is disabled / the order total is too small to earn even 1 point.
    /// </summary>
    Task<LoyaltyTransaction?> EarnOnOrderAsync(Guid tenantId, Guid customerId, Guid orderId, decimal orderTotal);

    /// <summary>
    /// Redeem points against a sale. Validates against MinRedeemPoints, balance,
    /// and the MaxRedemptionPercentOfOrder ceiling. Throws BusinessRuleException on violation.
    /// </summary>
    Task<LoyaltyRedeemResult> RedeemAsync(
        Guid tenantId, Guid customerId, int points,
        Guid orderId, decimal orderTotal);

    /// <summary>Newest-first ledger history (capped at <paramref name="take"/>).</summary>
    Task<List<LoyaltyTransaction>> GetHistoryAsync(Guid tenantId, Guid customerId, int take = 50);

    /// <summary>
    /// Reverse a previously-earned bucket of points (e.g. when the source order is returned/refunded).
    /// </summary>
    Task<LoyaltyTransaction?> ReverseEarnAsync(Guid tenantId, Guid customerId, Guid orderId);
}

public class LoyaltyService : ILoyaltyService
{
    private readonly RetailDbContext _db;
    private readonly ILogger<LoyaltyService> _logger;

    public LoyaltyService(RetailDbContext db, ILogger<LoyaltyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<LoyaltySettings> GetSettingsAsync(Guid tenantId)
    {
        var settings = await _db.LoyaltySettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && !s.IsDeleted);

        if (settings == null)
        {
            settings = new LoyaltySettings(tenantId);
            _db.LoyaltySettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        return settings;
    }

    public async Task<LoyaltySettings> UpdateSettingsAsync(
        Guid tenantId,
        decimal? rupeesPerPoint,
        int? minRedeemPoints,
        decimal? pointValueRupees,
        decimal? maxRedemptionPercentOfOrder,
        bool? isEnabled)
    {
        var settings = await GetSettingsAsync(tenantId);
        settings.Update(rupeesPerPoint, minRedeemPoints, pointValueRupees, maxRedemptionPercentOfOrder, isEnabled);
        await _db.SaveChangesAsync();
        return settings;
    }

    public async Task<int> GetBalanceAsync(Guid tenantId, Guid customerId)
    {
        return await _db.LoyaltyTransactions
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.CustomerId == customerId && !t.IsDeleted)
            .SumAsync(t => (int?)t.Points) ?? 0;
    }

    public async Task<LoyaltyTransaction?> EarnOnOrderAsync(
        Guid tenantId, Guid customerId, Guid orderId, decimal orderTotal)
    {
        if (customerId == Guid.Empty) return null;   // walk-in sale — no customer to credit
        if (orderTotal <= 0) return null;

        var settings = await GetSettingsAsync(tenantId);
        if (!settings.IsEnabled) return null;

        var points = (int)Math.Floor(orderTotal / settings.RupeesPerPoint);
        if (points <= 0) return null;

        var entry = new LoyaltyTransaction(
            tenantId, customerId, points, LoyaltyReason.EarnedOnOrder,
            orderId, rupeesValue: null,
            note: $"Earned on order ({orderTotal:N2} @ 1 pt / Rs {settings.RupeesPerPoint:N0})");

        _db.LoyaltyTransactions.Add(entry);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Loyalty earned: Tenant={TenantId}, Customer={CustomerId}, Points={Points}, Order={OrderId}",
            tenantId, customerId, points, orderId);

        return entry;
    }

    public async Task<LoyaltyRedeemResult> RedeemAsync(
        Guid tenantId, Guid customerId, int points,
        Guid orderId, decimal orderTotal)
    {
        if (customerId == Guid.Empty)
            throw new BusinessRuleException("Cannot redeem points on a walk-in sale (no customer attached).");
        if (points <= 0)
            throw new BusinessRuleException("Redeem points must be > 0.");

        var settings = await GetSettingsAsync(tenantId);
        if (!settings.IsEnabled)
            throw new BusinessRuleException("Loyalty is disabled for this tenant.");
        if (points < settings.MinRedeemPoints)
            throw new BusinessRuleException(
                $"At least {settings.MinRedeemPoints} points are required to redeem (attempted {points}).");

        var balance = await GetBalanceAsync(tenantId, customerId);
        if (balance < points)
            throw new BusinessRuleException(
                $"Insufficient point balance. Balance is {balance}, attempted redemption {points}.");

        var rupees = points * settings.PointValueRupees;
        var ceiling = orderTotal * (settings.MaxRedemptionPercentOfOrder / 100m);
        if (rupees > ceiling)
            throw new BusinessRuleException(
                $"Redemption capped at {settings.MaxRedemptionPercentOfOrder}% of order " +
                $"(Rs {ceiling:N2}). Reduce points and try again.");

        var entry = new LoyaltyTransaction(
            tenantId, customerId, -points, LoyaltyReason.Redeemed,
            orderId, rupees,
            note: $"Redeemed {points} pts = Rs {rupees:N2}");

        _db.LoyaltyTransactions.Add(entry);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Loyalty redeemed: Tenant={TenantId}, Customer={CustomerId}, Points={Points}, Rupees={Rupees}, Order={OrderId}",
            tenantId, customerId, points, rupees, orderId);

        return new LoyaltyRedeemResult(points, rupees, entry.Id);
    }

    public async Task<List<LoyaltyTransaction>> GetHistoryAsync(
        Guid tenantId, Guid customerId, int take = 50)
    {
        return await _db.LoyaltyTransactions
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.CustomerId == customerId && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAt)
            .Take(take)
            .ToListAsync();
    }

    public async Task<LoyaltyTransaction?> ReverseEarnAsync(Guid tenantId, Guid customerId, Guid orderId)
    {
        var earned = await _db.LoyaltyTransactions
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId
                     && t.CustomerId == customerId
                     && t.OrderId == orderId
                     && t.Reason == LoyaltyReason.EarnedOnOrder
                     && !t.IsDeleted)
            .FirstOrDefaultAsync();

        if (earned == null) return null;

        var reversal = new LoyaltyTransaction(
            tenantId, customerId, -earned.Points,
            LoyaltyReason.ReversedByReturn,
            orderId, rupeesValue: null,
            note: "Reversed: source order returned");

        _db.LoyaltyTransactions.Add(reversal);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Loyalty reversed: Tenant={TenantId}, Customer={CustomerId}, Points={Points}, Order={OrderId}",
            tenantId, customerId, earned.Points, orderId);

        return reversal;
    }
}
