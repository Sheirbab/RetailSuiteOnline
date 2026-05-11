using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;

namespace RetailSuite.Infrastructure.Modules.Inventory.Services
{
    public class InventoryService
    {
        private readonly RetailDbContext _db;
        private readonly ILogger<InventoryService> _logger;

        public InventoryService(RetailDbContext db, ILogger<InventoryService> logger)
        {
            _db = db;
            _logger = logger;
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
            _logger.LogInformation("Adjusting stock for ProductVariantId {ProductVariantId}: {QuantityChange} units ({TransactionType})", 
                productVariantId, quantityChange, transactionType);

            var inventoryItem = await _db.InventoryItems
                .FirstOrDefaultAsync(i => i.ProductVariantId == productVariantId);
            var variant = await _db.ProductVariants
                .FirstOrDefaultAsync(v => v.Id == productVariantId);

            if (variant == null)
                throw new InvalidOperationException("Product variant not found.");

            if (inventoryItem == null)
            {
                _logger.LogInformation("Creating new inventory item for ProductVariantId {ProductVariantId}", productVariantId);
                inventoryItem = new InventoryItem(productVariantId);
                _db.InventoryItems.Add(inventoryItem);
                await _db.SaveChangesAsync();
            }

            // Prevent negative stock
            if (inventoryItem.CurrentStock + quantityChange < 0)
            {
                _logger.LogWarning("Stock adjustment failed: insufficient stock for ProductVariantId {ProductVariantId}. Current: {CurrentStock}, Change: {QuantityChange}", 
                    productVariantId, inventoryItem.CurrentStock, quantityChange);
                throw new InvalidOperationException("Insufficient stock.");
            }

            var ledgerEntry = new InventoryTransaction(
                inventoryItem.Id,
                productVariantId,
                quantityChange,
                transactionType,
                referenceId,
                notes);

            inventoryItem.ApplyTransaction(quantityChange);
            variant.StockQuantity = inventoryItem.CurrentStock;
            variant.AverageCost = inventoryItem.AverageCost;

            _db.InventoryTransactions.Add(ledgerEntry);

            await _db.SaveChangesAsync();

            _logger.LogInformation("Stock adjustment completed for ProductVariantId {ProductVariantId}. New stock level: {NewStock}", 
                productVariantId, inventoryItem.CurrentStock);
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
            var variant = await _db.ProductVariants
                .FirstOrDefaultAsync(v => v.Id == productVariantId);

            if (variant == null)
                throw new InvalidOperationException("Product variant not found.");

            if (inventoryItem == null)
            {
                inventoryItem = new InventoryItem(productVariantId);
                _db.InventoryItems.Add(inventoryItem);
                await _db.SaveChangesAsync();
            }

            inventoryItem.ReceiveStock(quantity, unitCost);
            variant.StockQuantity = inventoryItem.CurrentStock;
            variant.AverageCost = inventoryItem.AverageCost;

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
}
