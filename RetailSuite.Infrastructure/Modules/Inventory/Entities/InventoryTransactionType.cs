namespace RetailSuite.Infrastructure.Modules.Inventory.Entities;

public enum InventoryTransactionType
{
    Purchase = 1,
    Sale = 2,
    AdjustmentIncrease = 3,
    AdjustmentDecrease = 4,
    Return = 5,
    Transfer = 6
}