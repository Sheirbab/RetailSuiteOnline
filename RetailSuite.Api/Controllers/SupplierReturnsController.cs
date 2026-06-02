using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.SupplierReturns.Entities;
using RetailSuite.Infrastructure.Modules.SupplierReturns.Services;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Endpoints for returning goods to a supplier. Lifecycle: Draft → Submitted →
/// Completed (or Cancelled). Completing a return deducts inventory and issues
/// a SupplierCreditNote against the supplier.
/// </summary>
[ApiController]
[Route("api/supplier-returns")]
[Authorize(Policy = "StaffOrAdmin")]
public class SupplierReturnsController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly ISupplierReturnService _service;

    public SupplierReturnsController(RetailDbContext db, ISupplierReturnService service)
    {
        _db      = db;
        _service = service;
    }

    // -------------------------------------------------------------
    // GET /api/supplier-returns?status=Submitted&supplierId=
    // -------------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] Guid? supplierId)
    {
        var q = _db.SupplierReturns.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<SupplierReturnStatus>(status, ignoreCase: true, out var s))
            q = q.Where(r => r.Status == s);

        if (supplierId.HasValue)
            q = q.Where(r => r.SupplierId == supplierId.Value);

        var rows = await q
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                r.ReturnNumber,
                r.SupplierId,
                SupplierName = _db.Suppliers
                                  .Where(s => s.Id == r.SupplierId)
                                  .Select(s => s.Name)
                                  .FirstOrDefault(),
                Status   = r.Status.ToString(),
                Reason   = r.Reason.ToString(),
                r.TotalValue,
                r.CreatedAt,
                r.SubmittedAt,
                r.CompletedAt,
                ItemCount = r.Items.Count
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(rows));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var r = await _db.SupplierReturns
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (r == null)
            return NotFound(ApiResponse<object>.Fail("Return not found."));

        var supplier = await _db.Suppliers
            .Where(s => s.Id == r.SupplierId)
            .Select(s => new { s.Id, s.Name, s.Phone })
            .FirstOrDefaultAsync();

        string? creditNoteNumber = null;
        decimal? creditAmount = null;
        if (r.Status == SupplierReturnStatus.Completed)
        {
            var cn = await _db.SupplierCreditNotes.FirstOrDefaultAsync(c => c.SupplierReturnId == r.Id);
            creditNoteNumber = cn?.CreditNoteNumber;
            creditAmount     = cn?.Amount;
        }

        return Ok(ApiResponse<object>.Ok(new
        {
            r.Id,
            r.ReturnNumber,
            Supplier = supplier,
            r.SourceReceivingOrderId,
            Status   = r.Status.ToString(),
            Reason   = r.Reason.ToString(),
            r.Notes,
            r.Currency,
            r.TotalValue,
            r.CreatedAt,
            r.SubmittedAt,
            r.CompletedAt,
            r.CancelledAt,
            CreditNoteNumber = creditNoteNumber,
            CreditAmount     = creditAmount,
            Items = r.Items.Select(i => new
            {
                i.Id,
                i.ProductVariantId,
                i.Sku,
                i.Quantity,
                i.UnitCost,
                LineTotal = i.LineTotal,
                i.Notes
            })
        }));
    }

    // -------------------------------------------------------------
    // POST /api/supplier-returns           (creates a Draft)
    // -------------------------------------------------------------
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateSupplierReturnRequest request)
    {
        if (request.SupplierId == Guid.Empty)
            return BadRequest(ApiResponse<object>.Fail("SupplierId is required."));

        if (!Enum.TryParse<SupplierReturnReason>(request.Reason, ignoreCase: true, out var reason))
            return BadRequest(ApiResponse<object>.Fail("Invalid Reason."));

        var ret = await _service.CreateDraftAsync(
            request.SupplierId, reason, request.SourceReceivingOrderId, request.Notes);

        return Ok(ApiResponse<object>.Ok(new
        {
            ret.Id,
            ret.ReturnNumber,
            Status = ret.Status.ToString()
        }));
    }

    // -------------------------------------------------------------
    // POST /api/supplier-returns/{id}/items
    // -------------------------------------------------------------
    [HttpPost("{id:guid}/items")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AddLine(Guid id, [FromBody] AddSupplierReturnLineRequest request)
    {
        if (request.ProductVariantId == Guid.Empty)
            return BadRequest(ApiResponse<object>.Fail("ProductVariantId is required."));
        if (request.Quantity <= 0)
            return BadRequest(ApiResponse<object>.Fail("Quantity must be positive."));

        var line = await _service.AddLineAsync(
            id, request.ProductVariantId, request.Quantity, request.UnitCost, request.Notes);

        return Ok(ApiResponse<object>.Ok(new
        {
            line.Id,
            line.Sku,
            line.Quantity,
            line.UnitCost,
            line.LineTotal
        }));
    }

    // -------------------------------------------------------------
    // DELETE /api/supplier-returns/{returnId}/items/{lineId}
    // -------------------------------------------------------------
    [HttpDelete("{returnId:guid}/items/{lineId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> RemoveLine(Guid returnId, Guid lineId)
    {
        var ret = await _db.SupplierReturns
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == returnId);
        if (ret == null)
            return NotFound(ApiResponse<object>.Fail("Return not found."));

        ret.RemoveItem(lineId);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { Removed = lineId }));
    }

    // -------------------------------------------------------------
    // POST /api/supplier-returns/{id}/submit
    // -------------------------------------------------------------
    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Submit(Guid id)
    {
        await _service.SubmitAsync(id);
        return Ok(ApiResponse<object>.Ok(new { Submitted = id }));
    }

    // -------------------------------------------------------------
    // POST /api/supplier-returns/{id}/complete
    // -------------------------------------------------------------
    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Complete(Guid id)
    {
        var credit = await _service.CompleteAsync(id);
        return Ok(ApiResponse<object>.Ok(new
        {
            ReturnId         = id,
            CreditNoteNumber = credit.CreditNoteNumber,
            CreditAmount     = credit.Amount
        }));
    }

    // -------------------------------------------------------------
    // POST /api/supplier-returns/{id}/cancel
    // -------------------------------------------------------------
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        await _service.CancelAsync(id);
        return Ok(ApiResponse<object>.Ok(new { Cancelled = id }));
    }

    // -------------------------------------------------------------
    // GET /api/supplier-returns/credit-notes?supplierId=
    // -------------------------------------------------------------
    [HttpGet("credit-notes")]
    public async Task<IActionResult> ListCreditNotes([FromQuery] Guid? supplierId)
    {
        var q = _db.SupplierCreditNotes.AsQueryable();
        if (supplierId.HasValue) q = q.Where(c => c.SupplierId == supplierId.Value);

        var rows = await q
            .OrderByDescending(c => c.IssuedAt)
            .Select(c => new
            {
                c.Id,
                c.CreditNoteNumber,
                c.SupplierId,
                SupplierName = _db.Suppliers
                                  .Where(s => s.Id == c.SupplierId)
                                  .Select(s => s.Name)
                                  .FirstOrDefault(),
                c.Amount,
                c.AppliedAmount,
                Remaining = c.Amount - c.AppliedAmount,
                c.IssuedAt,
                c.SupplierReturnId
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(rows));
    }
}

public class CreateSupplierReturnRequest
{
    public Guid    SupplierId             { get; set; }
    public string  Reason                 { get; set; } = "Damaged";
    public Guid?   SourceReceivingOrderId { get; set; }
    public string? Notes                  { get; set; }
}

public class AddSupplierReturnLineRequest
{
    public Guid    ProductVariantId { get; set; }
    public int     Quantity         { get; set; }
    public decimal? UnitCost        { get; set; }
    public string? Notes            { get; set; }
}
