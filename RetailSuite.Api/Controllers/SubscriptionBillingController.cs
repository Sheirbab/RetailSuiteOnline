using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Subscriptions.Dtos;
using RetailSuite.Infrastructure.Modules.Subscriptions.Services;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Tenant-facing billing endpoints (list invoices, view invoice, pay invoice).
/// SuperAdmin actions (manual mark-paid for bank transfer / cash reconciliation) live here too.
/// </summary>
[ApiController]
[Route("api/billing")]
public class SubscriptionBillingController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly ISubscriptionBillingService _billing;
    private readonly ITenantContext _tenantContext;

    public SubscriptionBillingController(
        RetailDbContext db,
        ISubscriptionBillingService billing,
        ITenantContext tenantContext)
    {
        _db = db;
        _billing = billing;
        _tenantContext = tenantContext;
    }

    // -------------------------------------------------------------
    // GET /api/billing/invoices  — tenant admin
    // -------------------------------------------------------------
    [HttpGet("invoices")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetInvoices()
    {
        var tenantId = RequireTenantId();

        var invoices = await _db.SubscriptionInvoices
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId && !i.IsDeleted)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => i.ToResponse())
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(invoices));
    }

    // -------------------------------------------------------------
    // GET /api/billing/invoices/{id}  — tenant admin
    // -------------------------------------------------------------
    [HttpGet("invoices/{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetInvoice(Guid id)
    {
        var tenantId = RequireTenantId();

        var invoice = await _db.SubscriptionInvoices
            .IgnoreQueryFilters()
            .Where(i => i.Id == id && i.TenantId == tenantId)
            .Select(i => i.ToResponse())
            .FirstOrDefaultAsync();

        if (invoice == null)
            return NotFound(ApiResponse<object>.Fail("Invoice not found."));

        var payments = await _db.SubscriptionPayments
            .IgnoreQueryFilters()
            .Where(p => p.InvoiceId == id && p.TenantId == tenantId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => p.ToResponse())
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { Invoice = invoice, Payments = payments }));
    }

    // -------------------------------------------------------------
    // POST /api/billing/invoices/{id}/pay  — tenant admin
    // -------------------------------------------------------------
    [HttpPost("invoices/{id:guid}/pay")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> PayInvoice(Guid id, [FromBody] PayInvoiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PaymentMethod))
            return BadRequest(ApiResponse<object>.Fail("PaymentMethod is required."));

        var tenantId = RequireTenantId();

        var payment = await _billing.PayInvoiceAsync(tenantId, id, request.PaymentMethod);
        return Ok(ApiResponse<object>.Ok(payment.ToResponse()));
    }

    // -------------------------------------------------------------
    // POST /api/billing/invoices/{id}/mark-paid  — SuperAdmin
    //   (manual reconciliation for bank transfer / cash)
    // -------------------------------------------------------------
    [HttpPost("invoices/{id:guid}/mark-paid")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> MarkPaid(Guid id, [FromBody] MarkPaidRequest request)
    {
        // SuperAdmin route — we need the invoice's tenantId, not the caller's.
        var invoice = await _db.SubscriptionInvoices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == id);
        if (invoice == null)
            return NotFound(ApiResponse<object>.Fail("Invoice not found."));

        var payment = await _billing.RecordManualPaymentAsync(
            invoice.TenantId, id, request.PaymentMethod, request.ExternalReference);

        return Ok(ApiResponse<object>.Ok(payment.ToResponse()));
    }

    private Guid RequireTenantId()
    {
        return _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");
    }
}
