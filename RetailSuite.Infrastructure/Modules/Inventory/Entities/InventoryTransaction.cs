using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Inventory.Entities;

public class InventoryTransaction : TenantEntity
{
    public Guid InventoryItemId { get; private set; }
    public Guid ProductVariantId { get; private set; }

    /// <summary>The branch / shop where this movement happened.</summary>
    public Guid LocationId { get; private set; }

    public int QuantityChange { get; private set; }

    public InventoryTransactionType TransactionType { get; private set; }

    public string? ReferenceId { get; private set; }
    public string? Notes { get; private set; }

    private InventoryTransaction() { }

    public InventoryTransaction(
        Guid inventoryItemId,
        Guid productVariantId,
        Guid locationId,
        int quantityChange,
        InventoryTransactionType type,
        string? referenceId = null,
        string? notes = null)
    {
        if (locationId == Guid.Empty)
            throw new ArgumentException("LocationId is required.", nameof(locationId));

        InventoryItemId  = inventoryItemId;
        ProductVariantId = productVariantId;
        LocationId       = locationId;
        QuantityChange   = quantityChange;
        TransactionType  = type;
        ReferenceId      = referenceId;
        Notes            = notes;
    }
}