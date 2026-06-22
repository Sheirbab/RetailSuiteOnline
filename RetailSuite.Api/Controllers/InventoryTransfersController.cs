using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Transfers.Entities;
using RetailSuite.Infrastructure.Modules.Transfers.Services;
using RetailSuite.Shared;
using RetailSuite.Api.Authorization;
using RetailSuite.Infrastructure.Modules.Identity.Entities;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Move stock from one branch to another. Lifecycle: Draft → InTransit → Received
/// (or Cancelled from Draft / InTransit). Submitting deducts source stock immediately;
/// receiving adds it at the destination.
/// </summary>
[ApiController]
[Route("api/inventory-transfers")]
[RequirePermission(Permissions.InventoryTransfer)]
public class InventoryTransfersController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly IInventoryTransferService _service;

    public InventoryTransfersController(RetailDbContext db, IInventoryTransferService service)
    {
        _db      = db;
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] Guid? locationId)
    {
        var q = _db.InventoryTransfers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<TransferStatus>(status, ignoreCase: true, out var s))
            q = q.Where(t => t.Status == s);
        if (locationId.HasValue)
            q = q.Where(t => t.SourceLocationId == locationId.Value || t.DestinationLocationId == locationId.Value);

        var rows = await q
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                t.TransferNumber,
                t.SourceLocationId,
                SourceName = _db.Locations.Where(l => l.Id == t.SourceLocationId).Select(l => l.Name).FirstOrDefault(),
                t.DestinationLocationId,
                DestinationName = _db.Locations.Where(l => l.Id == t.DestinationLocationId).Select(l => l.Name).FirstOrDefault(),
                Status     = t.Status.ToString(),
                t.TotalValue,
                t.CreatedAt,
                t.SubmittedAt,
                t.ReceivedAt,
                ItemCount  = t.Items.Count
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(rows));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var t = await _db.InventoryTransfers
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (t == null)
            return NotFound(ApiResponse<object>.Fail("Transfer not found."));

        var sourceName = await _db.Locations.Where(l => l.Id == t.SourceLocationId).Select(l => l.Name).FirstOrDefaultAsync();
        var destName   = await _db.Locations.Where(l => l.Id == t.DestinationLocationId).Select(l => l.Name).FirstOrDefaultAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            t.Id,
            t.TransferNumber,
            t.SourceLocationId,
            SourceName        = sourceName,
            t.DestinationLocationId,
            DestinationName   = destName,
            Status            = t.Status.ToString(),
            t.Notes,
            t.TotalValue,
            t.CreatedAt,
            t.SubmittedAt,
            t.ReceivedAt,
            t.CancelledAt,
            Items = t.Items.Select(i => new
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

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateTransferRequest request)
    {
        var t = await _service.CreateDraftAsync(request.SourceLocationId, request.DestinationLocationId, request.Notes);
        return Ok(ApiResponse<object>.Ok(new { t.Id, t.TransferNumber, Status = t.Status.ToString() }));
    }

    [HttpPost("{id:guid}/items")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AddLine(Guid id, [FromBody] AddTransferLineRequest request)
    {
        var line = await _service.AddLineAsync(id, request.ProductVariantId, request.Quantity, request.Notes);
        return Ok(ApiResponse<object>.Ok(new { line.Id, line.Sku, line.Quantity, line.UnitCost, line.LineTotal }));
    }

    [HttpDelete("{transferId:guid}/items/{lineId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> RemoveLine(Guid transferId, Guid lineId)
    {
        await _service.RemoveLineAsync(transferId, lineId);
        return Ok(ApiResponse<object>.Ok(new { Removed = lineId }));
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Submit(Guid id)
    {
        await _service.SubmitAsync(id);
        return Ok(ApiResponse<object>.Ok(new { Submitted = id }));
    }

    [HttpPost("{id:guid}/receive")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Receive(Guid id)
    {
        await _service.ReceiveAsync(id);
        return Ok(ApiResponse<object>.Ok(new { Received = id }));
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        await _service.CancelAsync(id);
        return Ok(ApiResponse<object>.Ok(new { Cancelled = id }));
    }
}

public class CreateTransferRequest
{
    public Guid    SourceLocationId      { get; set; }
    public Guid    DestinationLocationId { get; set; }
    public string? Notes                 { get; set; }
}

public class AddTransferLineRequest
{
    public Guid    ProductVariantId { get; set; }
    public int     Quantity         { get; set; }
    public string? Notes            { get; set; }
}
