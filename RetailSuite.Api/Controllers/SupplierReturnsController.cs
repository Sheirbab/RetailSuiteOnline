using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Receiving.Entities;
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

        var sourcePoNumber = r.SourceReceivingOrderId.HasValue
            ? await _db.ReceivingOrders
                .Where(o => o.Id == r.SourceReceivingOrderId.Value)
                .Select(o => o.OrderNumber)
                .FirstOrDefaultAsync()
            : null;

        return Ok(ApiResponse<object>.Ok(new
        {
            r.Id,
            r.ReturnNumber,
            Supplier = supplier,
            r.SourceReceivingOrderId,
            SourcePoNumber = sourcePoNumber,
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
    // POST /api/supplier-returns/{id}/pull-from-source
    // One-click: copy received lines from the source PO into the return.
    // -------------------------------------------------------------
    [HttpPost("{id:guid}/pull-from-source")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> PullFromSource(Guid id)
    {
        var added = await _service.PullFromSourceAsync(id);
        return Ok(ApiResponse<object>.Ok(new { LinesAdded = added }));
    }

    // -------------------------------------------------------------
    // GET /api/supplier-returns/source-pos?supplierId=
    // Helper for the create-modal: which receiving orders can we pull from?
    // -------------------------------------------------------------
    [HttpGet("source-pos")]
    public async Task<IActionResult> ListSourcePos([FromQuery] Guid supplierId)
    {
        if (supplierId == Guid.Empty)
            return BadRequest(ApiResponse<object>.Fail("supplierId is required."));

        var rows = await _db.ReceivingOrders
            .Where(o => o.SupplierId == supplierId
                     && (o.Status == ReceivingStatus.Closed
                      || o.Status == ReceivingStatus.PartiallyReceived
                      || o.Status == ReceivingStatus.Open))
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                Status        = o.Status.ToString(),
                o.ReceivedTotal,
                o.CreatedAt
            })
            .Take(50)
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(rows));
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
    // POST /api/supplier-returns/credit-notes/{id}/apply
    // Apply (part of) a credit note's remaining balance against a receiving order.
    // -------------------------------------------------------------
    [HttpPost("credit-notes/{id:guid}/apply")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ApplyCredit(Guid id, [FromBody] ApplyCreditRequest request)
    {
        if (request.ReceivingOrderId == Guid.Empty)
            return BadRequest(ApiResponse<object>.Fail("ReceivingOrderId is required."));
        if (request.Amount <= 0)
            return BadRequest(ApiResponse<object>.Fail("Amount must be positive."));

        var app = await _service.ApplyCreditAsync(id, request.ReceivingOrderId, request.Amount, request.Notes);
        var credit = await _db.SupplierCreditNotes.FirstOrDefaultAsync(c => c.Id == id);

        return Ok(ApiResponse<object>.Ok(new
        {
            ApplicationId    = app.Id,
            Amount           = app.Amount,
            RemainingOnNote  = credit?.Remaining ?? 0m
        }));
    }

    // -------------------------------------------------------------
    // GET /api/supplier-returns/credit-notes/available?supplierId=
    // Lists credit notes for a supplier that still have a remaining balance.
    // -------------------------------------------------------------
    [HttpGet("credit-notes/available")]
    public async Task<IActionResult> AvailableCredit([FromQuery] Guid supplierId)
    {
        if (supplierId == Guid.Empty)
            return BadRequest(ApiResponse<object>.Fail("supplierId is required."));

        var rows = await _db.SupplierCreditNotes
            .Where(c => c.SupplierId == supplierId && c.AppliedAmount < c.Amount)
            .OrderBy(c => c.IssuedAt)
            .Select(c => new
            {
                c.Id,
                c.CreditNoteNumber,
                c.Amount,
                c.AppliedAmount,
                Remaining = c.Amount - c.AppliedAmount,
                c.IssuedAt
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            SupplierId  = supplierId,
            TotalRemaining = rows.Sum(r => r.Remaining),
            Notes = rows
        }));
    }

    // -------------------------------------------------------------
    // GET /api/supplier-returns/credit-notes/applications?receivingOrderId=
    // Lists how much credit has been applied to a given receiving order.
    // -------------------------------------------------------------
    [HttpGet("credit-notes/applications")]
    public async Task<IActionResult> ListApplications([FromQuery] Guid? receivingOrderId)
    {
        var q = _db.SupplierCreditApplications.AsQueryable();
        if (receivingOrderId.HasValue) q = q.Where(a => a.ReceivingOrderId == receivingOrderId.Value);

        var rows = await q
            .OrderByDescending(a => a.AppliedAt)
            .Select(a => new
            {
                a.Id,
                a.CreditNoteId,
                CreditNoteNumber = _db.SupplierCreditNotes
                                     .Where(c => c.Id == a.CreditNoteId)
                                     .Select(c => c.CreditNoteNumber)
                                     .FirstOrDefault(),
                a.ReceivingOrderId,
                a.Amount,
                a.AppliedAt,
                a.Notes
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            TotalApplied = rows.Sum(r => r.Amount),
            Applications = rows
        }));
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

public class ApplyCreditRequest
{
    public Guid    ReceivingOrderId { get; set; }
    public decimal Amount           { get; set; }
    public string? Notes            { get; set; }
}
