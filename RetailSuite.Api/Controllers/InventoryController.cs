using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Inventory.Dtos;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Inventory.Services;
using RetailSuite.Api.Authorization;
using RetailSuite.Infrastructure.Modules.Identity.Entities;

namespace RetailSuite.Api.Controllers;

[RequirePermission(Permissions.InventoryView)]
[ApiController]
[Route("api/inventory")]
public class InventoryController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly InventoryService _inventoryService;

    public InventoryController(
        RetailDbContext db,
        InventoryService inventoryService)
    {
        _db = db;
        _inventoryService = inventoryService;
    }
    [HttpGet("all")]
    public async Task<IActionResult> GetAll(int page = 1, int pageSize = 50, Guid? locationId = null)
    {
        var query = _db.ProductVariants
            .Include(v => v.Product)
            .AsQueryable();

        var total = await query.CountAsync();

        // Stock comes from the rollup (v.StockQuantity) unless a location filter
        // is supplied — in which case we read the per-location InventoryItem.
        var rows = await query
            .OrderBy(v => v.SKU)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new
            {
                v.Id,
                ProductName  = v.Product.Name,
                SKU          = v.SKU,
                TotalStock   = v.StockQuantity,
                AverageCost  = v.AverageCost,
                LocationStock = locationId.HasValue
                    ? _db.InventoryItems
                          .Where(i => i.ProductVariantId == v.Id && i.LocationId == locationId.Value)
                          .Select(i => (int?)i.CurrentStock)
                          .FirstOrDefault()
                    : null
            })
            .ToListAsync();

        var items = rows.Select(r => new InventoryItemDto
        {
            Id           = r.Id,
            ProductName  = r.ProductName,
            SKU          = r.SKU,
            CurrentStock = locationId.HasValue ? (r.LocationStock ?? 0) : r.TotalStock,
            AverageCost  = r.AverageCost
        }).ToList();

        return Ok(new { Total = total, Page = page, PageSize = pageSize, Items = items });
    }

    // ---------------------------------------
    // Manual Adjustment
    // ---------------------------------------
    [HttpPost("adjust")]
    public async Task<IActionResult> AdjustStock(AdjustStockRequest request)
    {
        await _inventoryService.AdjustStockAsync(
            request.ProductVariantId,
            request.Quantity,
            request.Type,
            request.Reference,
            request.Reason,
            request.LocationId);

        return Ok();
    }

    // ---------------------------------------
    // Get Current Stock
    // ---------------------------------------
    [HttpGet("{variantId}")]
    public async Task<IActionResult> GetStock(Guid variantId)
    {
        var item = await _db.InventoryItems
            .FirstOrDefaultAsync(i => i.ProductVariantId == variantId);

        if (item == null)
            return NotFound();

        return Ok(new
        {
            item.ProductVariantId,
            item.CurrentStock,
            item.AverageCost,
            item.TotalStockValue
        });
    }

    // ---------------------------------------
    // Get Ledger
    // ---------------------------------------
    //[HttpGet("{variantId}/transactions")]
    //public async Task<IActionResult> GetTransactions(Guid variantId)
    //{
    //    var transactions = await _inventoryService.GetTransactionsAsync(variantId);
    //    return Ok(transactions);
    //}
    [HttpGet("transactions/{variantId}")]
    public async Task<IActionResult> GetTransactions(Guid variantId)
    {
        var transactions = await _db.InventoryTransactions
            .Where(t => t.ProductVariantId == variantId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return Ok(transactions);
    }

    [HttpGet("low-stock")]
    public async Task<IActionResult> GetLowStock(int threshold = 5)
    {
        var items = await _db.InventoryItems
            .Where(i => i.CurrentStock <= threshold)
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost("receive")]
    public async Task<IActionResult> ReceiveStock(ReceiveStockRequest request)
    {
        await _inventoryService.ReceiveStockAsync(
            request.ProductVariantId,
            request.Quantity,
            request.UnitCost,
            request.Reference);

        return Ok();
    }

    // ---------------------------------------
    // Ad-hoc bulk receive (no PO required)
    // ---------------------------------------
    /// <summary>
    /// Receive many variants at once without creating a formal receiving order.
    /// Useful for quick "truck arrived, just add the stock" workflows.
    /// Each line writes its own InventoryTransaction sharing the same ReferenceId
    /// (typically the supplier invoice number) so the batch is auditable.
    /// </summary>
    [HttpPost("receive-bulk")]
    public async Task<IActionResult> ReceiveStockBulk(
        [FromBody] RetailSuite.Infrastructure.Modules.Receiving.Dtos.AdHocBulkReceiveRequest request)
    {
        if (request?.Items == null || request.Items.Count == 0)
            return BadRequest(new { error = "Items must not be empty." });

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var line in request.Items)
            {
                if (line.Quantity <= 0) continue;
                await _inventoryService.ReceiveStockAsync(
                    line.ProductVariantId,
                    line.Quantity,
                    line.UnitCost,
                    request.ReferenceId);
            }
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        return Ok(new { Received = request.Items.Count, Reference = request.ReferenceId });
    }
    [HttpGet("valuation")]
    public async Task<IActionResult> GetValuation()
    {
        var items = await _db.InventoryItems.ToListAsync();

        var totalValue = items.Sum(i => i.TotalStockValue);

        return Ok(new
        {
            TotalInventoryValue = totalValue,
            Items = items.Select(i => new
            {
                i.ProductVariantId,
                i.CurrentStock,
                i.AverageCost,
                i.TotalStockValue
            })
        });
    }

}