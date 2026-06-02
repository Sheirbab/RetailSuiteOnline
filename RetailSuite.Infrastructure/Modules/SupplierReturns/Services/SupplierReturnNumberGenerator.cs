using Microsoft.EntityFrameworkCore;

namespace RetailSuite.Infrastructure.Modules.SupplierReturns.Services;

/// <summary>
/// Produces "SR-{yyyyMM}-{0001}" per-tenant unique supplier-return numbers,
/// and "SCN-{yyyyMM}-{0001}" for credit-notes. Sequence resets monthly per tenant.
/// </summary>
public interface ISupplierReturnNumberGenerator
{
    Task<string> NextReturnNumberAsync(Guid tenantId);
    Task<string> NextCreditNoteNumberAsync(Guid tenantId);
}

public class SupplierReturnNumberGenerator : ISupplierReturnNumberGenerator
{
    private readonly RetailDbContext _db;
    public SupplierReturnNumberGenerator(RetailDbContext db) => _db = db;

    public Task<string> NextReturnNumberAsync(Guid tenantId)
        => NextAsync(tenantId, prefix: "SR-", lookupExisting: () =>
            _db.SupplierReturns.IgnoreQueryFilters()
                .Where(r => r.TenantId == tenantId)
                .Select(r => r.ReturnNumber));

    public Task<string> NextCreditNoteNumberAsync(Guid tenantId)
        => NextAsync(tenantId, prefix: "SCN-", lookupExisting: () =>
            _db.SupplierCreditNotes.IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId)
                .Select(c => c.CreditNoteNumber));

    private async Task<string> NextAsync(Guid tenantId, string prefix, Func<IQueryable<string>> lookupExisting)
    {
        var now      = DateTime.UtcNow;
        var fullPrefix = $"{prefix}{now:yyyyMM}-";

        var existing = await lookupExisting()
            .Where(n => n.StartsWith(fullPrefix))
            .ToListAsync();

        var maxSeq = 0;
        foreach (var num in existing)
        {
            var lastDash = num.LastIndexOf('-');
            if (lastDash <= 0) continue;
            if (int.TryParse(num[(lastDash + 1)..], out var seq) && seq > maxSeq)
                maxSeq = seq;
        }

        return $"{fullPrefix}{(maxSeq + 1):0000}";
    }
}
