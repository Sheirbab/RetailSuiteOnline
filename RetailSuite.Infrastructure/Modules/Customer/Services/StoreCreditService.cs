using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Customer.Entities;

namespace RetailSuite.Infrastructure.Modules.Customer.Services;

/// <summary>
/// Issues and redeems customer store credit. The balance is always the sum of
/// <see cref="StoreCreditTransaction.Amount"/> ledger entries — never stored separately.
/// </summary>
public interface IStoreCreditService
{
    /// <summary>Current balance for the customer (sum of all ledger entries).</summary>
    Task<decimal> GetBalanceAsync(Guid tenantId, Guid customerId);

    /// <summary>Issue credit (positive). Raises a ledger entry with the supplied reason / note.</summary>
    Task<StoreCreditTransaction> IssueAsync(
        Guid tenantId, Guid customerId, decimal amount,
        StoreCreditReason reason, string? note, Guid? orderId, Guid? createdByUserId);

    /// <summary>Redeem credit (positive amount → negative ledger entry). Throws if balance is insufficient.</summary>
    Task<StoreCreditTransaction> RedeemAsync(
        Guid tenantId, Guid customerId, decimal amount,
        Guid orderId, Guid? createdByUserId, string? note = null);

    /// <summary>Newest-first ledger history (capped at <paramref name="take"/>).</summary>
    Task<List<StoreCreditTransaction>> GetHistoryAsync(Guid tenantId, Guid customerId, int take = 50);
}

public class StoreCreditService : IStoreCreditService
{
    private readonly RetailDbContext _db;
    private readonly ILogger<StoreCreditService> _logger;

    public StoreCreditService(RetailDbContext db, ILogger<StoreCreditService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<decimal> GetBalanceAsync(Guid tenantId, Guid customerId)
    {
        return await _db.StoreCreditTransactions
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.CustomerId == customerId && !t.IsDeleted)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;
    }

    public async Task<StoreCreditTransaction> IssueAsync(
        Guid tenantId, Guid customerId, decimal amount,
        StoreCreditReason reason, string? note, Guid? orderId, Guid? createdByUserId)
    {
        if (amount <= 0)
            throw new BusinessRuleException("Issued credit amount must be positive.");

        var entry = new StoreCreditTransaction(
            tenantId, customerId, amount, reason, note, orderId, createdByUserId);

        _db.StoreCreditTransactions.Add(entry);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Store credit issued: Tenant={TenantId}, Customer={CustomerId}, Amount={Amount}, Reason={Reason}",
            tenantId, customerId, amount, reason);

        return entry;
    }

    public async Task<StoreCreditTransaction> RedeemAsync(
        Guid tenantId, Guid customerId, decimal amount,
        Guid orderId, Guid? createdByUserId, string? note = null)
    {
        if (amount <= 0)
            throw new BusinessRuleException("Redeem amount must be positive.");

        var balance = await GetBalanceAsync(tenantId, customerId);
        if (balance < amount)
            throw new BusinessRuleException(
                $"Insufficient store credit. Balance is {balance:N2}, attempted redemption {amount:N2}.");

        // Stored as a negative amount so SUM(Amount) gives the running balance.
        var entry = new StoreCreditTransaction(
            tenantId, customerId, -amount,
            StoreCreditReason.RedeemedAgainstSale,
            note ?? "Redeemed at sale",
            orderId, createdByUserId);

        _db.StoreCreditTransactions.Add(entry);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Store credit redeemed: Tenant={TenantId}, Customer={CustomerId}, Amount={Amount}, Order={OrderId}",
            tenantId, customerId, amount, orderId);

        return entry;
    }

    public async Task<List<StoreCreditTransaction>> GetHistoryAsync(
        Guid tenantId, Guid customerId, int take = 50)
    {
        return await _db.StoreCreditTransactions
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.CustomerId == customerId && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAt)
            .Take(take)
            .ToListAsync();
    }
}
