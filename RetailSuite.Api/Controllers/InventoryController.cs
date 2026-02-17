using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Inventory.Services;

namespace RetailSuite.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/inventory")]
public class InventoryController : ControllerBase
{
    private readonly InventoryService _inventoryService;

    public InventoryController(InventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    // ---------------------------------------
    // Manual Adjustment
    // ---------------------------------------
    [HttpPost("adjust")]
    public async Task<IActionResult> Adjust(
        Guid productVariantId,
        int quantityChange,
        string? notes)
    {
        var type = quantityChange > 0
            ? InventoryTransactionType.AdjustmentIncrease
            : InventoryTransactionType.AdjustmentDecrease;

        await _inventoryService.AdjustStockAsync(
            productVariantId,
            quantityChange,
            type,
            null,
            notes);

        return Ok();
    }

    // ---------------------------------------
    // Get Current Stock
    // ---------------------------------------
    [HttpGet("{variantId}")]
    public async Task<IActionResult> GetStock(Guid variantId)
    {
        var stock = await _inventoryService.GetStockAsync(variantId);
        return Ok(stock);
    }

    // ---------------------------------------
    // Get Ledger
    // ---------------------------------------
    [HttpGet("{variantId}/transactions")]
    public async Task<IActionResult> GetTransactions(Guid variantId)
    {
        var transactions = await _inventoryService.GetTransactionsAsync(variantId);
        return Ok(transactions);
    }
}