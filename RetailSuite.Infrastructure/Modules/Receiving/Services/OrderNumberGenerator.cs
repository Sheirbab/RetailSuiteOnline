using Microsoft.EntityFrameworkCore;

namespace RetailSuite.Infrastructure.Modules.Receiving.Services;

/// <summary>
/// Produces "PO-{yyyyMM}-{0001}" per-tenant unique receiving-order numbers.
/// Sequence resets monthly per tenant — same shape as InvoiceNumberGenerator.
/// </summary>
public interface IReceivingOrderNumberGenerator
{
    Task<string> NextAsync(Guid tenantId);
}

public class ReceivingOrderNumberGenerator : IReceivingOrderNumberGenerator
{
    private readonly RetailDbContext _db;
    public ReceivingOrderNumberGenerator(RetailDbContext db) => _db = db;

    public async Task<string> NextAsync(Guid tenantId)
    {
        var now    = DateTime.UtcNow;
        var prefix = $"PO-{now:yyyyMM}-";

        var existing = await _db.ReceivingOrders
            .IgnoreQueryFilters()
            .Where(o => o.TenantId == tenantId && o.OrderNumber.StartsWith(prefix))
            .Select(o => o.OrderNumber)
            .ToListAsync();

        var maxSeq = 0;
        foreach (var num in existing)
        {
            var lastDash = num.LastIndexOf('-');
            if (lastDash <= 0) continue;
            if (int.TryParse(num[(lastDash + 1)..], out var seq) && seq > maxSeq)
                maxSeq = seq;
        }

        return $"{prefix}{(maxSeq + 1):0000}";
    }
}
