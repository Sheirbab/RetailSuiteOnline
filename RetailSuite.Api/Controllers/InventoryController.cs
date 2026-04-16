using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Inventory.Dtos;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Inventory.Services;

namespace RetailSuite.Api.Controllers;

//[Authorize(Policy = "StaffOrAdmin")]
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
    public async Task<IActionResult> GetAll()
    {
        var data = await _db.ProductVariants
            .Include(v => v.Product)
            .Select(v => new InventoryItemDto
            {
                Id = v.Id,
                ProductName = v.Product.Name,
                SKU = v.SKU,
                CurrentStock = v.StockQuantity,
                AverageCost = v.AverageCost
            })
            .ToListAsync();

        return Ok(data);
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
            request.Reason);

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