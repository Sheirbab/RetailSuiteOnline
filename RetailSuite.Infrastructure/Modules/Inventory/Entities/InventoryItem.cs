using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Inventory.Entities;

public class InventoryItem : TenantEntity
{
    public Guid ProductVariantId { get; private set; }

    public int CurrentStock { get; private set; }

    public int LowStockThreshold { get; private set; }

    private readonly List<InventoryTransaction> _transactions = new();
    public IReadOnlyCollection<InventoryTransaction> Transactions => _transactions;

    private InventoryItem() { }

    public InventoryItem(Guid productVariantId, int lowStockThreshold = 5)
    {
        ProductVariantId = productVariantId;
        LowStockThreshold = lowStockThreshold;
        CurrentStock = 0;
    }

    public void ApplyTransaction(int quantityChange)
    {
        CurrentStock += quantityChange;
    }
}