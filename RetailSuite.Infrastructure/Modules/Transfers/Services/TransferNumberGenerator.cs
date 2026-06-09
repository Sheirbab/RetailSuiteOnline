using Microsoft.EntityFrameworkCore;

namespace RetailSuite.Infrastructure.Modules.Transfers.Services;

/// <summary>Produces "TRF-{yyyyMM}-{0001}" per-tenant unique transfer numbers.</summary>
public interface ITransferNumberGenerator
{
    Task<string> NextAsync(Guid tenantId);
}

public class TransferNumberGenerator : ITransferNumberGenerator
{
    private readonly RetailDbContext _db;
    public TransferNumberGenerator(RetailDbContext db) => _db = db;

    public async Task<string> NextAsync(Guid tenantId)
    {
        var now    = DateTime.UtcNow;
        var prefix = $"TRF-{now:yyyyMM}-";

        var existing = await _db.InventoryTransfers
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.TransferNumber.StartsWith(prefix))
            .Select(t => t.TransferNumber)
            .ToListAsync();

        var maxSeq = 0;
        foreach (var num in existing)
        {
            var dash = num.LastIndexOf('-');
            if (dash <= 0) continue;
            if (int.TryParse(num[(dash + 1)..], out var seq) && seq > maxSeq) maxSeq = seq;
        }

        return $"{prefix}{(maxSeq + 1):0000}";
    }
}
