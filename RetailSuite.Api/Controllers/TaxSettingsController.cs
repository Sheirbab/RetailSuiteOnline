using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Tax.Entities;
using RetailSuite.Shared;
using RetailSuite.Api.Authorization;
using RetailSuite.Infrastructure.Modules.Identity.Entities;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Tenant tax / FBR settings — the legal identity printed on every compliant invoice
/// plus invoice-numbering preferences. One row per tenant; auto-seeded on tenant create.
/// </summary>
[ApiController]
[Route("api/tax-settings")]
[RequirePermission(Permissions.TaxSettings)]
public class TaxSettingsController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly ITenantContext _tenantContext;

    public TaxSettingsController(RetailDbContext db, ITenantContext tenantContext)
    {
        _db            = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var s = await GetOrCreateAsync();
        return Ok(ApiResponse<object>.Ok(ToResponse(s)));
    }

    [HttpPatch]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update([FromBody] UpdateTaxSettingsRequest request)
    {
        var s = await GetOrCreateAsync();

        if (request.LegalIdentity != null)
        {
            s.UpdateLegalIdentity(
                request.LegalIdentity.Ntn,
                request.LegalIdentity.Strn,
                request.LegalIdentity.BusinessNameAsRegistered,
                request.LegalIdentity.RegisteredAddress);
        }

        if (request.InvoiceNumbering != null)
        {
            s.UpdateInvoiceNumbering(
                request.InvoiceNumbering.InvoicePrefix,
                request.InvoiceNumbering.FiscalYearStartMonth);
        }

        if (request.PricingConvention != null)
        {
            s.UpdatePricingConvention(
                request.PricingConvention.PricesIncludeTax,
                request.PricingConvention.DefaultTaxRate);
        }

        if (request.FbrIntegration != null)
        {
            s.UpdateFbrIntegration(
                request.FbrIntegration.FbrEnabled,
                request.FbrIntegration.FbrPosId,
                request.FbrIntegration.FbrStatus);
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(ToResponse(s)));
    }

    // ----- helpers ------------------------------------------------------

    private async Task<TaxSettings> GetOrCreateAsync()
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");

        var s = await _db.TaxSettings.FirstOrDefaultAsync(t => t.TenantId == tenantId);
        if (s == null)
        {
            s = new TaxSettings(tenantId);
            _db.TaxSettings.Add(s);
            await _db.SaveChangesAsync();
        }
        return s;
    }

    private static object ToResponse(TaxSettings s) => new
    {
        s.Id,
        LegalIdentity = new
        {
            s.Ntn,
            s.Strn,
            s.BusinessNameAsRegistered,
            s.RegisteredAddress
        },
        InvoiceNumbering = new
        {
            s.InvoicePrefix,
            s.FiscalYearStartMonth
        },
        PricingConvention = new
        {
            s.PricesIncludeTax,
            s.DefaultTaxRate
        },
        FbrIntegration = new
        {
            s.FbrEnabled,
            s.FbrPosId,
            s.FbrStatus
        }
    };
}

public class UpdateTaxSettingsRequest
{
    public LegalIdentityDto?     LegalIdentity     { get; set; }
    public InvoiceNumberingDto?  InvoiceNumbering  { get; set; }
    public PricingConventionDto? PricingConvention { get; set; }
    public FbrIntegrationDto?    FbrIntegration    { get; set; }

    public class LegalIdentityDto
    {
        public string? Ntn { get; set; }
        public string? Strn { get; set; }
        public string? BusinessNameAsRegistered { get; set; }
        public string? RegisteredAddress { get; set; }
    }

    public class InvoiceNumberingDto
    {
        public string? InvoicePrefix { get; set; }
        public int?    FiscalYearStartMonth { get; set; }
    }

    public class PricingConventionDto
    {
        public bool    PricesIncludeTax { get; set; }
        public decimal DefaultTaxRate { get; set; }
    }

    public class FbrIntegrationDto
    {
        public bool    FbrEnabled { get; set; }
        public string? FbrPosId { get; set; }
        public string? FbrStatus { get; set; }
    }
}
