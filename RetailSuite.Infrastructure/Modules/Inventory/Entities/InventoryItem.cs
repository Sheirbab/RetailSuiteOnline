using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Inventory.Entities;

public class InventoryItem : TenantEntity
{
    public Guid ProductVariantId { get; private set; }

    public int CurrentStock { get; private set; }

    public int LowStockThreshold { get; private set; }
    public decimal AverageCost { get; private set; }
    public decimal TotalStockValue { get; private set; }

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
    public void ReceiveStock(int quantity, decimal unitCost)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive.");

        var purchaseValue = quantity * unitCost;

        var newTotalValue = TotalStockValue + purchaseValue;
        var newStock = CurrentStock + quantity;

        CurrentStock = newStock;
        TotalStockValue = newTotalValue;

        AverageCost = newStock == 0
            ? 0
            : newTotalValue / newStock;
    }

    public decimal IssueStock(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive.");

        if (CurrentStock < quantity)
            throw new InvalidOperationException("Insufficient stock.");

        var costAmount = AverageCost * quantity;

        CurrentStock -= quantity;
        TotalStockValue -= costAmount;

        if (CurrentStock == 0)
            AverageCost = 0;

        return costAmount; // important for COGS
    }
}