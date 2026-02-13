using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Modules.Inventory.Services;
using RetailSuite.Modules.Orders.Entities;

namespace RetailSuite.Modules.Orders.Services;

public class OrderService
{
    private readonly RetailDbContext _db;
    private readonly InventoryService _inventoryService;

    public OrderService(
        RetailDbContext db,
        InventoryService inventoryService)
    {
        _db = db;
        _inventoryService = inventoryService;
    }

    // ---------------------------------------
    // Confirm Order
    // ---------------------------------------
    public async Task ConfirmOrderAsync(Guid orderId)
    {
        using var transaction = await _db.Database.BeginTransactionAsync();

        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            throw new Exception("Order not found.");

        if (order.Status != OrderStatus.Draft)
            throw new Exception("Only draft orders can be confirmed.");

        // Deduct inventory for each item
        foreach (var item in order.Items)
        {
            await _inventoryService.AdjustStockAsync(
                item.ProductVariantId,
                -item.Quantity,
                InventoryTransactionType.Sale,
                order.Id.ToString(),
                "Order confirmation");
        }

        order.Confirm();

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}