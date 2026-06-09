using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Transfers.Entities;

/// <summary>
/// One line on an <see cref="InventoryTransfer"/> — one variant + qty + cost snapshot.
/// Quantity is always positive; the service applies the negative sign when deducting
/// from source and the positive when adding to destination.
/// </summary>
public class InventoryTransferItem : TenantEntity
{
    public Guid InventoryTransferId { get; private set; }
    public Guid ProductVariantId { get; private set; }

    /// <summary>SKU snapshot at the time the line was added.</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>Units to move from source to destination.</summary>
    public int Quantity { get; private set; }

    /// <summary>Snapshot of the average cost at submit time. Used for transfer value reporting and accounting.</summary>
    public decimal UnitCost { get; private set; }

    /// <summary>Free-text — e.g. "fragile" or "lot ABC-123".</summary>
    public string? Notes { get; private set; }

    public decimal LineTotal => UnitCost * Quantity;

    private InventoryTransferItem() { }

    public InventoryTransferItem(
        Guid tenantId,
        Guid inventoryTransferId,
        Guid productVariantId,
        string sku,
        int quantity,
        decimal unitCost,
        string? notes = null)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive.", nameof(quantity));
        if (unitCost < 0)
            throw new ArgumentException("UnitCost cannot be negative.", nameof(unitCost));

        Id                  = Guid.NewGuid();
        CreatedAt           = DateTime.UtcNow;
        TenantId            = tenantId;
        InventoryTransferId = inventoryTransferId;
        ProductVariantId    = productVariantId;
        Sku                 = sku ?? string.Empty;
        Quantity            = quantity;
        UnitCost            = unitCost;
        Notes               = notes;
    }

    public void SetNotes(string? notes) => Notes = notes;
}
