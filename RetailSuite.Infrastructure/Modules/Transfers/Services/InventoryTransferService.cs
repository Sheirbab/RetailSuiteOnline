using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Inventory.Services;
using RetailSuite.Infrastructure.Modules.Transfers.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Transfers.Services;

public interface IInventoryTransferService
{
    Task<InventoryTransfer> CreateDraftAsync(Guid sourceLocationId, Guid destinationLocationId, string? notes);

    Task<InventoryTransferItem> AddLineAsync(Guid transferId, Guid productVariantId, int quantity, string? notes);

    Task RemoveLineAsync(Guid transferId, Guid lineId);

    /// <summary>Submit (Draft → InTransit). Deducts stock at source.</summary>
    Task SubmitAsync(Guid transferId);

    /// <summary>Receive (InTransit → Received). Adds stock at destination.</summary>
    Task ReceiveAsync(Guid transferId);

    /// <summary>Cancel. If currently InTransit, restores source stock.</summary>
    Task CancelAsync(Guid transferId);
}

public class InventoryTransferService : IInventoryTransferService
{
    private readonly RetailDbContext _db;
    private readonly InventoryService _inventory;
    private readonly ITransferNumberGenerator _numbers;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<InventoryTransferService> _logger;

    public InventoryTransferService(
        RetailDbContext db,
        InventoryService inventory,
        ITransferNumberGenerator numbers,
        ITenantContext tenantContext,
        ILogger<InventoryTransferService> logger)
    {
        _db            = db;
        _inventory     = inventory;
        _numbers       = numbers;
        _tenantContext = tenantContext;
        _logger        = logger;
    }

    public async Task<InventoryTransfer> CreateDraftAsync(
        Guid sourceLocationId, Guid destinationLocationId, string? notes)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");

        if (sourceLocationId == destinationLocationId)
            throw new BusinessRuleException("Source and destination must differ.");

        var sourceExists = await _db.Locations.AnyAsync(l => l.Id == sourceLocationId);
        var destExists   = await _db.Locations.AnyAsync(l => l.Id == destinationLocationId);
        if (!sourceExists)
            throw new NotFoundException("Source Location", sourceLocationId);
        if (!destExists)
            throw new NotFoundException("Destination Location", destinationLocationId);

        var number = await _numbers.NextAsync(tenantId);
        var transfer = new InventoryTransfer(tenantId, number, sourceLocationId, destinationLocationId);
        if (!string.IsNullOrWhiteSpace(notes)) transfer.SetNotes(notes);

        _db.InventoryTransfers.Add(transfer);
        await _db.SaveChangesAsync();
        return transfer;
    }

    public async Task<InventoryTransferItem> AddLineAsync(
        Guid transferId, Guid productVariantId, int quantity, string? notes)
    {
        var transfer = await LoadAsync(transferId);
        if (transfer.Status != TransferStatus.Draft)
            throw new BusinessRuleException("Only Draft transfers can be modified.");
        if (quantity <= 0)
            throw new BusinessRuleException("Quantity must be positive.");

        var variant = await _db.ProductVariants.FirstOrDefaultAsync(v => v.Id == productVariantId)
            ?? throw new NotFoundException("ProductVariant", productVariantId);

        // Validate against source stock — can't queue more than what's there.
        var sourceStock = await _db.InventoryItems
            .Where(i => i.ProductVariantId == productVariantId && i.LocationId == transfer.SourceLocationId)
            .Select(i => (int?)i.CurrentStock)
            .FirstOrDefaultAsync() ?? 0;

        // Sum already-queued quantity for this variant on this transfer.
        var alreadyQueued = transfer.Items.Where(i => i.ProductVariantId == productVariantId).Sum(i => i.Quantity);

        if (alreadyQueued + quantity > sourceStock)
            throw new BusinessRuleException(
                $"Source branch has only {sourceStock} on hand for {variant.SKU} (already on this transfer: {alreadyQueued}).");

        var line = new InventoryTransferItem(
            transfer.TenantId,
            transfer.Id,
            variant.Id,
            variant.SKU,
            quantity,
            variant.AverageCost,
            notes);

        transfer.AddItem(line);
        await _db.SaveChangesAsync();
        return line;
    }

    public async Task RemoveLineAsync(Guid transferId, Guid lineId)
    {
        var transfer = await LoadAsync(transferId);
        transfer.RemoveItem(lineId);
        await _db.SaveChangesAsync();
    }

    public async Task SubmitAsync(Guid transferId)
    {
        var transfer = await LoadAsync(transferId);

        // Re-verify source stock once more — could have moved since the draft was built.
        foreach (var line in transfer.Items)
        {
            var stock = await _inventory.GetStockAsync(line.ProductVariantId, transfer.SourceLocationId);
            if (stock < line.Quantity)
                throw new BusinessRuleException(
                    $"Cannot submit — source has {stock} of {line.Sku}, transfer needs {line.Quantity}.");
        }

        // Deduct from source. Each line writes its own InventoryTransaction (audit trail).
        foreach (var line in transfer.Items)
        {
            await _inventory.AdjustStockAsync(
                productVariantId: line.ProductVariantId,
                quantityChange:   -line.Quantity,
                transactionType:  InventoryTransactionType.Transfer,
                referenceId:      transfer.TransferNumber,
                notes:            $"Transfer out → {transfer.TransferNumber}",
                locationId:       transfer.SourceLocationId);
        }

        transfer.MarkInTransit();
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Transfer {Number} submitted: {SourceId} → {DestId}, {LineCount} lines",
            transfer.TransferNumber, transfer.SourceLocationId, transfer.DestinationLocationId, transfer.Items.Count);
    }

    public async Task ReceiveAsync(Guid transferId)
    {
        var transfer = await LoadAsync(transferId);
        if (transfer.Status != TransferStatus.InTransit)
            throw new BusinessRuleException("Only InTransit transfers can be received.");

        // Credit the destination using ReceiveStockAsync so AverageCost reflects the
        // transferred goods' cost basis.
        foreach (var line in transfer.Items)
        {
            await _inventory.ReceiveStockAsync(
                productVariantId: line.ProductVariantId,
                quantity:         line.Quantity,
                unitCost:         line.UnitCost,
                referenceId:      transfer.TransferNumber,
                locationId:       transfer.DestinationLocationId);
        }

        transfer.MarkReceived();
        await _db.SaveChangesAsync();
    }

    public async Task CancelAsync(Guid transferId)
    {
        var transfer = await LoadAsync(transferId);

        if (transfer.Status == TransferStatus.Received)
            throw new BusinessRuleException("Received transfers cannot be cancelled.");

        // If goods are in transit, restore the source stock.
        if (transfer.Status == TransferStatus.InTransit)
        {
            foreach (var line in transfer.Items)
            {
                await _inventory.AdjustStockAsync(
                    productVariantId: line.ProductVariantId,
                    quantityChange:   line.Quantity,
                    transactionType:  InventoryTransactionType.Transfer,
                    referenceId:      transfer.TransferNumber,
                    notes:            $"Transfer cancelled → {transfer.TransferNumber} (restored to source)",
                    locationId:       transfer.SourceLocationId);
            }
        }

        transfer.MarkCancelled();
        await _db.SaveChangesAsync();
    }

    private async Task<InventoryTransfer> LoadAsync(Guid id) =>
        await _db.InventoryTransfers
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new NotFoundException("InventoryTransfer", id);
}
