using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure.Modules.Tax.Entities;

namespace RetailSuite.Infrastructure.Modules.Tax.Services;

/// <summary>
/// Produces FBR-compliant sequential invoice numbers per tenant.
///
/// Format: "{Prefix}-FY{YY}{YY}-{0000001}"   e.g.  "INV-FY2526-0000042"
///         where YYYY is the fiscal year start, YY+YY are the last two digits of
///         the fiscal year start and end (e.g. FY 2025-26 → "2526").
///
/// Sequence resets on the configured fiscal-year start month per tenant
/// (default = July, Pakistan). Uniqueness is enforced at the DB level
/// (unique index on TenantId + InvoiceNumber).
/// </summary>
public interface ISalesInvoiceNumberGenerator
{
    /// <summary>Generate the next invoice number for the given tenant, honouring its TaxSettings.</summary>
    Task<string> NextAsync(Guid tenantId, DateTime issuedAt);
}

public class SalesInvoiceNumberGenerator : ISalesInvoiceNumberGenerator
{
    private readonly RetailDbContext _db;
    public SalesInvoiceNumberGenerator(RetailDbContext db) => _db = db;

    public async Task<string> NextAsync(Guid tenantId, DateTime issuedAt)
    {
        // Read tenant's tax settings (or fall back to defaults).
        var settings = await _db.TaxSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId);

        var prefix  = settings?.InvoicePrefix  ?? "INV";
        var fyStart = settings?.FiscalYearStartMonth ?? 7;

        var (fyTag, fyStartDate, fyEndDate) = ComputeFiscalYear(issuedAt, fyStart);
        var fullPrefix = $"{prefix}-FY{fyTag}-";

        // Find the max sequence already issued for this prefix in this fiscal year window.
        var existing = await _db.Orders
            .IgnoreQueryFilters()
            .Where(o => o.TenantId == tenantId
                     && o.InvoiceNumber != null
                     && o.InvoiceIssuedAt >= fyStartDate
                     && o.InvoiceIssuedAt <  fyEndDate
                     && o.InvoiceNumber.StartsWith(fullPrefix))
            .Select(o => o.InvoiceNumber!)
            .ToListAsync();

        var maxSeq = 0;
        foreach (var num in existing)
        {
            var dash = num.LastIndexOf('-');
            if (dash <= 0) continue;
            if (int.TryParse(num[(dash + 1)..], out var seq) && seq > maxSeq)
                maxSeq = seq;
        }

        return $"{fullPrefix}{(maxSeq + 1):0000000}";
    }

    /// <summary>
    /// Compute the fiscal-year tag and date window. For PK default (start=July):
    ///   8 Aug 2025  → tag "2526", window [2025-07-01, 2026-07-01)
    ///   3 Mar 2026  → tag "2526", window [2025-07-01, 2026-07-01)
    ///   1 Aug 2026  → tag "2627", window [2026-07-01, 2027-07-01)
    /// </summary>
    public static (string Tag, DateTime FyStart, DateTime FyEnd) ComputeFiscalYear(DateTime when, int fyStartMonth)
    {
        // Calendar year the FY started in for this date.
        var fyStartYear = when.Month >= fyStartMonth ? when.Year : when.Year - 1;
        var fyEndYear   = fyStartYear + 1;

        var start = new DateTime(fyStartYear, fyStartMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        var end   = start.AddYears(1);

        var tag = $"{fyStartYear % 100:D2}{fyEndYear % 100:D2}";
        return (tag, start, end);
    }
}
