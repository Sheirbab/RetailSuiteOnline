using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Modules.Catalog.Entities;

namespace RetailSuite.Infrastructure.Modules.Inventory.Services
{
    /// <summary>
    /// All stock changes flow through here. Operations are scoped to a (variant, location)
    /// pair — if the caller omits a locationId, we fall back to the tenant's default location.
    /// After every change, the rollup denormalised onto <c>ProductVariant.StockQuantity</c>
    /// is recomputed as the sum across all locations so the catalogue / POS / receipts
    /// always see consistent totals.
    /// </summary>
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
            string? notes = null,
            Guid? locationId = null)
        {
            var resolvedLocation = await ResolveLocationAsync(locationId);

            _logger.LogInformation(
                "Adjusting stock for variant {ProductVariantId} at location {LocationId}: {QuantityChange} ({TransactionType})",
                productVariantId, resolvedLocation, quantityChange, transactionType);

            var inventoryItem = await _db.InventoryItems
                .FirstOrDefaultAsync(i => i.ProductVariantId == productVariantId
                                       && i.LocationId == resolvedLocation);
            var variant = await _db.ProductVariants
                .FirstOrDefaultAsync(v => v.Id == productVariantId);

            if (variant == null)
                throw new InvalidOperationException("Product variant not found.");

            if (inventoryItem == null)
            {
                _logger.LogInformation(
                    "Creating new inventory row for variant {ProductVariantId} at location {LocationId}",
                    productVariantId, resolvedLocation);
                inventoryItem = new InventoryItem(productVariantId, resolvedLocation);
                _db.InventoryItems.Add(inventoryItem);
                await _db.SaveChangesAsync();
            }

            // Prevent negative stock
            if (inventoryItem.CurrentStock + quantityChange < 0)
            {
                _logger.LogWarning(
                    "Stock adjust failed: insufficient stock for variant {ProductVariantId} at location {LocationId}. Current: {CurrentStock}, Change: {QuantityChange}",
                    productVariantId, resolvedLocation, inventoryItem.CurrentStock, quantityChange);
                throw new InvalidOperationException("Insufficient stock.");
            }

            var ledgerEntry = new InventoryTransaction(
                inventoryItem.Id,
                productVariantId,
                resolvedLocation,
                quantityChange,
                transactionType,
                referenceId,
                notes);

            inventoryItem.ApplyTransaction(quantityChange);
            _db.InventoryTransactions.Add(ledgerEntry);

            // Recompute the per-location average cost stamp on the row.
            variant.AverageCost = inventoryItem.AverageCost;

            await _db.SaveChangesAsync();

            // Rollup denormalised StockQuantity = SUM(InventoryItem.CurrentStock) across all locations.
            await RecomputeVariantRollupAsync(variant);

            _logger.LogInformation(
                "Stock adjustment complete for variant {ProductVariantId}. New stock at this location: {NewStock}",
                productVariantId, inventoryItem.CurrentStock);
        }

        // ----------------------------------------------------
        // Get Current Stock
        // ----------------------------------------------------
        /// <summary>Stock at a specific location (or the tenant's default when locationId is null).</summary>
        public async Task<int> GetStockAsync(Guid productVariantId, Guid? locationId = null)
        {
            var resolvedLocation = await ResolveLocationAsync(locationId);
            var inventoryItem = await _db.InventoryItems
                .FirstOrDefaultAsync(i => i.ProductVariantId == productVariantId
                                       && i.LocationId == resolvedLocation);

            return inventoryItem?.CurrentStock ?? 0;
        }

        /// <summary>Total stock for a variant across every location — the rollup.</summary>
        public async Task<int> GetTotalStockAsync(Guid productVariantId)
        {
            return await _db.InventoryItems
                .Where(i => i.ProductVariantId == productVariantId)
                .SumAsync(i => (int?)i.CurrentStock) ?? 0;
        }

        /// <summary>Per-location breakdown of stock for a variant.</summary>
        public async Task<List<LocationStockRow>> GetStockByLocationAsync(Guid productVariantId)
        {
            return await _db.InventoryItems
                .Where(i => i.ProductVariantId == productVariantId)
                .Join(_db.Locations,
                      i => i.LocationId,
                      l => l.Id,
                      (i, l) => new LocationStockRow
                      {
                          LocationId   = l.Id,
                          LocationCode = l.Code,
                          LocationName = l.Name,
                          CurrentStock = i.CurrentStock,
                          AverageCost  = i.AverageCost
                      })
                .ToListAsync();
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

        // ----------------------------------------------------
        // Receive (PO receipt) — preserves average-cost calculation
        // ----------------------------------------------------
        public async Task ReceiveStockAsync(
            Guid productVariantId,
            int quantity,
            decimal unitCost,
            string? referenceId = null,
            Guid? locationId = null)
        {
            var resolvedLocation = await ResolveLocationAsync(locationId);

            var inventoryItem = await _db.InventoryItems
                .FirstOrDefaultAsync(i => i.ProductVariantId == productVariantId
                                       && i.LocationId == resolvedLocation);
            var variant = await _db.ProductVariants
                .FirstOrDefaultAsync(v => v.Id == productVariantId);

            if (variant == null)
                throw new InvalidOperationException("Product variant not found.");

            if (inventoryItem == null)
            {
                inventoryItem = new InventoryItem(productVariantId, resolvedLocation);
                _db.InventoryItems.Add(inventoryItem);
                await _db.SaveChangesAsync();
            }

            inventoryItem.ReceiveStock(quantity, unitCost);
            variant.AverageCost = inventoryItem.AverageCost;

            var ledgerEntry = new InventoryTransaction(
                inventoryItem.Id,
                productVariantId,
                resolvedLocation,
                quantity,
                InventoryTransactionType.Purchase,
                referenceId,
                "Stock purchase");

            _db.InventoryTransactions.Add(ledgerEntry);
            await _db.SaveChangesAsync();
            await RecomputeVariantRollupAsync(variant);
        }

        // ----------------------------------------------------
        // Internals
        // ----------------------------------------------------

        /// <summary>
        /// If <paramref name="explicitLocationId"/> is provided, return it.
        /// Otherwise look up the tenant's default location.
        /// </summary>
        private async Task<Guid> ResolveLocationAsync(Guid? explicitLocationId)
        {
            if (explicitLocationId.HasValue && explicitLocationId.Value != Guid.Empty)
                return explicitLocationId.Value;

            var defaultLoc = await _db.Locations
                .Where(l => l.IsDefault && l.IsActive)
                .Select(l => (Guid?)l.Id)
                .FirstOrDefaultAsync();

            if (!defaultLoc.HasValue)
                throw new InvalidOperationException(
                    "No location specified and no default location is configured for this tenant.");

            return defaultLoc.Value;
        }

        /// <summary>Recompute the rollup denormalised onto ProductVariant.StockQuantity.</summary>
        private async Task RecomputeVariantRollupAsync(ProductVariant? variantOrNull)
        {
            if (variantOrNull == null) return;

            var total = await _db.InventoryItems
                .Where(i => i.ProductVariantId == variantOrNull.Id)
                .SumAsync(i => (int?)i.CurrentStock) ?? 0;

            if (variantOrNull.StockQuantity != total)
            {
                variantOrNull.StockQuantity = total;
                await _db.SaveChangesAsync();
            }
        }
    }

    public class LocationStockRow
    {
        public Guid    LocationId   { get; set; }
        public string  LocationCode { get; set; } = "";
        public string  LocationName { get; set; } = "";
        public int     CurrentStock { get; set; }
        public decimal AverageCost  { get; set; }
    }
}
