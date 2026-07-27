using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Receiving.Dtos;
using RetailSuite.Infrastructure.Modules.Receiving.Services;
using RetailSuite.Shared;
using RetailSuite.Api.Authorization;
using RetailSuite.Infrastructure.Modules.Identity.Entities;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Purchase / receiving order workflow.
/// Lifecycle: Draft (editable) → Open (submitted) → PartiallyReceived → Closed.
/// Inventory stock only changes when a receipt is recorded, not on submission.
/// </summary>
[ApiController]
[Route("api/receiving-orders")]
[RequirePermission(Permissions.ReceivingOrders)]
public class ReceivingOrdersController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly IReceivingOrderService _service;
    private readonly ITenantContext _tenantContext;

    public ReceivingOrdersController(
        RetailDbContext db,
        IReceivingOrderService service,
        ITenantContext tenantContext)
    {
        _db             = db;
        _service        = service;
        _tenantContext  = tenantContext;
    }

    // -------------------------------------------------------------
    // GET /api/receiving-orders?status=Open
    // -------------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] Guid? supplierId)
    {
        var q = _db.ReceivingOrders.Include(o => o.Items).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<Infrastructure.Modules.Receiving.Entities.ReceivingStatus>(status, ignoreCase: true, out var st))
        {
            q = q.Where(o => o.Status == st);
        }

        if (supplierId.HasValue)
            q = q.Where(o => o.SupplierId == supplierId.Value);

        var rows = await q
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => o.ToResponse())
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(rows));
    }

    // -------------------------------------------------------------
    // GET /api/receiving-orders/variants
    // -------------------------------------------------------------
    [HttpGet("variants")]
    public async Task<IActionResult> ListVariants()
    {
        var rows = await _db.ProductVariants
            .Include(v => v.Product)
            .Where(v => v.IsActive)
            .OrderBy(v => v.SKU)
            .Select(v => new
            {
                v.Id,
                ProductName = v.Product.Name,
                Sku = v.SKU,
                v.AverageCost
            })
            .ToListAsync();

        return Ok(new { Items = rows });
    }

    // -------------------------------------------------------------
    // GET /api/receiving-orders/{id}
    // -------------------------------------------------------------
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var tenantId = RequireTenantId();
        var order = await _service.GetByIdAsync(tenantId, id);
        if (order == null)
            return NotFound(ApiResponse<object>.Fail("Receiving order not found."));
        return Ok(ApiResponse<object>.Ok(order.ToResponse()));
    }

    // -------------------------------------------------------------
    // POST /api/receiving-orders   (Draft)
    // -------------------------------------------------------------
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateReceivingOrderRequest request)
    {
        var tenantId = RequireTenantId();
        var order = await _service.CreateDraftAsync(
            tenantId, request.SupplierId, request.SupplierReference, request.ExpectedDate, request.Notes,
            request.DestinationLocationId);
        return Ok(ApiResponse<object>.Ok(order.ToResponse()));
    }

    // -------------------------------------------------------------
    // POST /api/receiving-orders/{id}/lines
    // -------------------------------------------------------------
    [HttpPost("{id:guid}/lines")]
    public async Task<IActionResult> AddLine(Guid id, [FromBody] AddLineRequest request)
    {
        var tenantId = RequireTenantId();
        await _service.AddLineAsync(
            tenantId, id, request.ProductVariantId,
            request.ExpectedQuantity, request.UnitCost, request.Notes);
        var order = await _service.GetByIdAsync(tenantId, id);
        return Ok(ApiResponse<object>.Ok(order!.ToResponse()));
    }

    // -------------------------------------------------------------
    // DELETE /api/receiving-orders/{id}/lines/{lineId}
    // -------------------------------------------------------------
    [HttpDelete("{id:guid}/lines/{lineId:guid}")]
    public async Task<IActionResult> RemoveLine(Guid id, Guid lineId)
    {
        var tenantId = RequireTenantId();
        await _service.RemoveLineAsync(tenantId, id, lineId);
        var order = await _service.GetByIdAsync(tenantId, id);
        return Ok(ApiResponse<object>.Ok(order!.ToResponse()));
    }

    // -------------------------------------------------------------
    // POST /api/receiving-orders/{id}/submit  (Draft -> Open)
    // -------------------------------------------------------------
    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id)
    {
        var tenantId = RequireTenantId();
        await _service.SubmitAsync(tenantId, id);
        var order = await _service.GetByIdAsync(tenantId, id);
        return Ok(ApiResponse<object>.Ok(order!.ToResponse()));
    }

    // -------------------------------------------------------------
    // POST /api/receiving-orders/{id}/lines/{lineId}/receive
    // -------------------------------------------------------------
    [HttpPost("{id:guid}/lines/{lineId:guid}/receive")]
    public async Task<IActionResult> ReceiveLine(Guid id, Guid lineId, [FromBody] ReceiveLineRequest request)
    {
        var tenantId = RequireTenantId();
        await _service.ReceiveLineAsync(tenantId, id, lineId, request.ReceivedQuantity, request.Notes);
        var order = await _service.GetByIdAsync(tenantId, id);
        return Ok(ApiResponse<object>.Ok(order!.ToResponse()));
    }

    // -------------------------------------------------------------
    // POST /api/receiving-orders/{id}/receive-batch
    // -------------------------------------------------------------
    [HttpPost("{id:guid}/receive-batch")]
    public async Task<IActionResult> ReceiveBatch(Guid id, [FromBody] ReceiveBatchRequest request)
    {
        if (request.Items == null || request.Items.Count == 0)
            return BadRequest(ApiResponse<object>.Fail("Items must not be empty."));

        var tenantId = RequireTenantId();
        await _service.ReceiveBatchAsync(
            tenantId, id,
            request.Items.Select(i => (i.LineId, i.Quantity)));
        var order = await _service.GetByIdAsync(tenantId, id);
        return Ok(ApiResponse<object>.Ok(order!.ToResponse()));
    }

    // -------------------------------------------------------------
    // POST /api/receiving-orders/{id}/close
    // -------------------------------------------------------------
    [HttpPost("{id:guid}/close")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Close(Guid id)
    {
        var tenantId = RequireTenantId();
        await _service.CloseAsync(tenantId, id);
        var order = await _service.GetByIdAsync(tenantId, id);
        return Ok(ApiResponse<object>.Ok(order!.ToResponse()));
    }

    // -------------------------------------------------------------
    // POST /api/receiving-orders/{id}/cancel
    // -------------------------------------------------------------
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var tenantId = RequireTenantId();
        await _service.CancelAsync(tenantId, id);
        var order = await _service.GetByIdAsync(tenantId, id);
        return Ok(ApiResponse<object>.Ok(order!.ToResponse()));
    }

    private Guid RequireTenantId() =>
        _tenantContext.TenantId
        ?? throw new UnauthorizedAccessException("Tenant context missing.");
}
