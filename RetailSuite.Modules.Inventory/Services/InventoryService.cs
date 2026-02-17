using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;

namespace RetailSuite.Modules.Inventory.Services;

public class InventoryService
{
    private readonly RetailDbContext _db;

    public InventoryService(RetailDbContext db)
    {
        _db = db;
    }

    // ----------------------------------------------------
    // Adjust Stock (Generic Entry Point)
    // ----------------------------------------------------
    public async Task AdjustStockAsync(
        Guid productVariantId,
        int quantityChange,
        InventoryTransactionType transactionType,
        string? referenceId = null,
        string? notes = null)
    {

        var inventoryItem = await _db.InventoryItems
            .FirstOrDefaultAsync(i => i.ProductVariantId == productVariantId);

        if (inventoryItem == null)
        {
            inventoryItem = new InventoryItem(productVariantId);
            _db.InventoryItems.Add(inventoryItem);
            await _db.SaveChangesAsync();
        }

        // Prevent negative stock
        if (inventoryItem.CurrentStock + quantityChange < 0)
            throw new InvalidOperationException("Insufficient stock.");

        var ledgerEntry = new InventoryTransaction(
            inventoryItem.Id,
            productVariantId,
            quantityChange,
            transactionType,
            referenceId,
            notes);

        inventoryItem.ApplyTransaction(quantityChange);

        _db.InventoryTransactions.Add(ledgerEntry);

        await _db.SaveChangesAsync();
    }

    // ----------------------------------------------------
    // Get Current Stock
    // ----------------------------------------------------
    public async Task<int> GetStockAsync(Guid productVariantId)
    {
        var inventoryItem = await _db.InventoryItems
            .FirstOrDefaultAsync(i => i.ProductVariantId == productVariantId);

        return inventoryItem?.CurrentStock ?? 0;
    }

    // ----------------------------------------------------
    // Get Ledger History
    // ----------------------------------------------------
    public async Task<List<InventoryTransaction>> GetTransactionsAsync(
        Guid productVariantId)
    {
        return await _db.InventoryTransactions
            .Where(t => t.ProductVariantId == productVariantId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }
    public async Task ReceiveStockAsync(
    Guid productVariantId,
    int quantity,
    decimal unitCost,
    string? referenceId = null)
    {
        var inventoryItem = await _db.InventoryItems
            .FirstOrDefaultAsync(i => i.ProductVariantId == productVariantId);

        if (inventoryItem == null)
        {
            inventoryItem = new InventoryItem(productVariantId);
            _db.InventoryItems.Add(inventoryItem);
            await _db.SaveChangesAsync();
        }

        var purchaseValue = quantity * unitCost;

        var newTotalValue = inventoryItem.TotalStockValue + purchaseValue;
        var newStock = inventoryItem.CurrentStock + quantity;

        inventoryItem.ApplyTransaction(quantity);

        inventoryItem.ReceiveStock(quantity, unitCost);

        var ledgerEntry = new InventoryTransaction(
        inventoryItem.Id,
        productVariantId,
        quantity,
        InventoryTransactionType.Purchase,
        referenceId,
        "Stock purchase");

        _db.InventoryTransactions.Add(ledgerEntry);

        await _db.SaveChangesAsync();
    }
}