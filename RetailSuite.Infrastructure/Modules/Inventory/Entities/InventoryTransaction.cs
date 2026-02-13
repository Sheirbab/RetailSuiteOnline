using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Inventory.Entities;

public class InventoryTransaction : TenantEntity
{
    public Guid InventoryItemId { get; private set; }
    public Guid ProductVariantId { get; private set; }

    public int QuantityChange { get; private set; }

    public InventoryTransactionType TransactionType { get; private set; }

    public string? ReferenceId { get; private set; }
    public string? Notes { get; private set; }

    private InventoryTransaction() { }

    public InventoryTransaction(
        Guid inventoryItemId,
        Guid productVariantId,
        int quantityChange,
        InventoryTransactionType type,
        string? referenceId = null,
        string? notes = null)
    {
        InventoryItemId = inventoryItemId;
        ProductVariantId = productVariantId;
        QuantityChange = quantityChange;
        TransactionType = type;
        ReferenceId = referenceId;
        Notes = notes;
    }
}