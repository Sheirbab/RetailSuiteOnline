using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Inventory.Services;
using RetailSuite.Modules.Accounting.Services;
using RetailSuite.Modules.Orders.Entities;

namespace RetailSuite.Infrastructure.Modules.Orders.Services
{
    public class OrderService
    {
        private readonly RetailDbContext _db;
        private readonly InventoryService _inventoryService;
        private readonly AccountingService _accountingService;

        public OrderService(
        RetailDbContext db,
        InventoryService inventoryService,
        AccountingService accountingService)
        {
            _db = db;
            _inventoryService = inventoryService;
            _accountingService = accountingService;
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

            foreach (var item in order.Items)
            {
                await _inventoryService.AdjustStockAsync(
                    item.ProductVariantId,
                    -item.Quantity,
                    InventoryTransactionType.Sale,
                    order.Id.ToString(),
                    "Order confirmation");
            }

            // ---------------------------------------------------
            // Accounting Integration
            // ---------------------------------------------------

            // Fetch required accounts
            var arAccount = await _db.Accounts.FirstAsync(a => a.Code == "1200");
            var revenueAccount = await _db.Accounts.FirstAsync(a => a.Code == "4000");
            var inventoryAccount = await _db.Accounts.FirstAsync(a => a.Code == "1100");
            var cogsAccount = await _db.Accounts.FirstAsync(a => a.Code == "5000");

            decimal totalCogs = 0;

            foreach (var item in order.Items)
            {

                var inventoryItem = await _db.InventoryItems.FirstOrDefaultAsync(i => i.ProductVariantId == item.ProductVariantId);
                // This is the "sale"
                var costAmount = inventoryItem?.IssueStock(item.Quantity) ?? 0;

                totalCogs += costAmount;

                //var variant = await _db.ProductVariants.FirstAsync(v => v.Id == item.ProductVariantId);

                // totalCogs += variant.CostPrice * item.Quantity;
            }
            await _accountingService.CreateJournalEntryAsync(
                                                           order.Id.ToString(),
                                                           $"Sale Order {order.OrderNumber}",
                                                           new List<(Guid, decimal, decimal)>
                                                           {
                                                        (arAccount.Id, order.TotalAmount, 0),
                                                        (revenueAccount.Id, 0, order.TotalAmount),
                                                        (cogsAccount.Id, totalCogs, 0),
                                                        (inventoryAccount.Id, 0, totalCogs)
                                                           });
            order.Confirm();

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        // ---------------------------------------
        // Cancel Order
        // ---------------------------------------
        public async Task CancelOrderAsync(Guid orderId)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();

            var order = await _db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                throw new Exception("Order not found.");

            if (order.Status == OrderStatus.Cancelled)
                throw new Exception("Order already cancelled.");

            if (order.Status == OrderStatus.Completed)
                throw new Exception("Completed orders cannot be cancelled.");

            // If confirmed → restore stock
            if (order.Status == OrderStatus.Confirmed)
            {
                foreach (var item in order.Items)
                {
                    await _inventoryService.AdjustStockAsync(
                        item.ProductVariantId,
                        item.Quantity,
                        InventoryTransactionType.Return,
                        order.Id.ToString(),
                        "Order cancellation");
                }
            }

            order.Cancel();

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
    }
}
