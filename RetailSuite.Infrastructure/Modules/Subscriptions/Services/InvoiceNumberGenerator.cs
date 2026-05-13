using Microsoft.EntityFrameworkCore;

namespace RetailSuite.Infrastructure.Modules.Subscriptions.Services;

/// <summary>
/// Produces human-readable, per-tenant unique invoice numbers of the form
/// "INV-{yyyyMM}-{0001}". Sequence resets monthly per tenant.
/// </summary>
public interface IInvoiceNumberGenerator
{
    Task<string> NextAsync(Guid tenantId);
}

public class InvoiceNumberGenerator : IInvoiceNumberGenerator
{
    private readonly RetailDbContext _db;
    public InvoiceNumberGenerator(RetailDbContext db) => _db = db;

    public async Task<string> NextAsync(Guid tenantId)
    {
        var now    = DateTime.UtcNow;
        var prefix = $"INV-{now:yyyyMM}-";

        var lastSeq = await _db.SubscriptionInvoices
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId && i.InvoiceNumber.StartsWith(prefix))
            .Select(i => i.InvoiceNumber)
            .ToListAsync();

        var maxSeq = 0;
        foreach (var num in lastSeq)
        {
            // Parse trailing seq segment.
            var lastDash = num.LastIndexOf('-');
            if (lastDash <= 0) continue;
            if (int.TryParse(num[(lastDash + 1)..], out var seq) && seq > maxSeq)
                maxSeq = seq;
        }

        var next = maxSeq + 1;
        return $"{prefix}{next:0000}";
    }
}
