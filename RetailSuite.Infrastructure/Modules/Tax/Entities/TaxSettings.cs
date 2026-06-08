using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Tax.Entities;

/// <summary>
/// Per-tenant tax / FBR registration settings. One row per tenant.
///
/// Holds the legal-identity fields printed on every compliant invoice:
/// NTN, STRN, registered business name and address. Also the per-tenant
/// invoice numbering preferences (prefix, fiscal year start month) and the
/// pricing convention (prices-include-tax vs added at till).
///
/// Hooks for future PRAL / FBR real-time POS-integration via
/// <see cref="FbrPosId"/> and <see cref="FbrEnabled"/>.
/// </summary>
public class TaxSettings : TenantEntity
{
    // ---- Legal identity --------------------------------------------------

    /// <summary>National Tax Number — printed on every invoice when set.</summary>
    public string? Ntn { get; private set; }

    /// <summary>Sales Tax Registration Number — required if the tenant is sales-tax-registered.</summary>
    public string? Strn { get; private set; }

    /// <summary>Business name as registered with FBR — may differ from the tenant's display name.</summary>
    public string? BusinessNameAsRegistered { get; private set; }

    /// <summary>Registered address printed on invoices.</summary>
    public string? RegisteredAddress { get; private set; }

    // ---- Invoice numbering -----------------------------------------------

    /// <summary>Prefix used on invoice numbers — e.g. "INV" produces "INV-FY2526-000001".</summary>
    public string InvoicePrefix { get; private set; } = "INV";

    /// <summary>Month (1–12) the fiscal year starts on. Pakistan = 7 (July). Resets the invoice sequence.</summary>
    public int FiscalYearStartMonth { get; private set; } = 7;

    // ---- Pricing convention ----------------------------------------------

    /// <summary>
    /// True = prices on Variant are tax-inclusive (common in retail); tax is
    /// computed by extraction. False = prices are tax-exclusive; tax is added at till.
    /// Currently informational — the POS uses LineNet × TaxRate either way.
    /// </summary>
    public bool PricesIncludeTax { get; private set; } = false;

    /// <summary>Fallback tax rate for variants that don't have their own rate set.</summary>
    public decimal DefaultTaxRate { get; private set; } = 0m;

    // ---- Future PRAL / FBR integration -----------------------------------

    /// <summary>True once enrolled with FBR's PRAL POS-integration. Off by default.</summary>
    public bool FbrEnabled { get; private set; }

    /// <summary>FBR-issued POS identifier — assigned during PRAL enrolment.</summary>
    public string? FbrPosId { get; private set; }

    /// <summary>Last known status from PRAL (e.g. "Active", "Suspended"). Null until enrolled.</summary>
    public string? FbrStatus { get; private set; }

    private TaxSettings() { }

    /// <summary>Create a default (empty) TaxSettings for a tenant — used by the tenant seeder.</summary>
    public TaxSettings(Guid tenantId)
    {
        Id        = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        TenantId  = tenantId;
    }

    public void UpdateLegalIdentity(string? ntn, string? strn, string? businessName, string? address)
    {
        Ntn                      = string.IsNullOrWhiteSpace(ntn)          ? null : ntn.Trim();
        Strn                     = string.IsNullOrWhiteSpace(strn)         ? null : strn.Trim();
        BusinessNameAsRegistered = string.IsNullOrWhiteSpace(businessName) ? null : businessName.Trim();
        RegisteredAddress        = string.IsNullOrWhiteSpace(address)      ? null : address.Trim();
    }

    public void UpdateInvoiceNumbering(string? prefix, int? fiscalYearStartMonth)
    {
        if (!string.IsNullOrWhiteSpace(prefix))
            InvoicePrefix = prefix.Trim().ToUpperInvariant();
        if (fiscalYearStartMonth.HasValue && fiscalYearStartMonth.Value is >= 1 and <= 12)
            FiscalYearStartMonth = fiscalYearStartMonth.Value;
    }

    public void UpdatePricingConvention(bool pricesIncludeTax, decimal defaultTaxRate)
    {
        PricesIncludeTax = pricesIncludeTax;
        DefaultTaxRate   = Math.Max(0m, Math.Min(1m, defaultTaxRate));
    }

    public void UpdateFbrIntegration(bool enabled, string? posId, string? status)
    {
        FbrEnabled = enabled;
        FbrPosId   = string.IsNullOrWhiteSpace(posId) ? null : posId.Trim();
        FbrStatus  = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
    }
}
