using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Inventory.Services;
using RetailSuite.Infrastructure.Modules.Orders.Dtos;
using RetailSuite.Modules.Accounting.Services;
using RetailSuite.Modules.Orders.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Orders.Services
{
    public class OrderService
    {
        private readonly RetailDbContext _db;
        private readonly InventoryService _inventoryService;
        private readonly AccountingService _accountingService;
        private readonly ICurrentUserContext _currentUser;
        public OrderService(
        RetailDbContext db,
        InventoryService inventoryService,
        AccountingService accountingService,
        ICurrentUserContext currentUser)
        {
            _db = db;
            _inventoryService = inventoryService;
            _accountingService = accountingService;
            _currentUser = currentUser;
        }

        // ---------------------------------------
        // Confirm Order
        // ---------------------------------------
        public async Task ConfirmOrderAsync(Guid orderId)
        {
            var userId = _currentUser.UserId;

            var customer = await _db.Customers
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (customer == null)
                throw new Exception("Customer profile not found.");

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
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                throw new Exception("Order not found.");

            if (order.Status == OrderStatus.Cancelled)
                throw new Exception("Already cancelled.");

            // Draft → just cancel
            if (order.Status == OrderStatus.Draft)
            {
                order.Cancel();
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                return;
            }

            // ===============================
            // Reverse Inventory + Accounting
            // ===============================

            decimal totalCogs = 0;

            foreach (var item in order.Items)
            {
                var inventoryItem = await _db.InventoryItems
                    .FirstAsync(i => i.ProductVariantId == item.ProductVariantId);

                // Restore stock
                inventoryItem.ReceiveStock(item.Quantity, inventoryItem.AverageCost);

                totalCogs += inventoryItem.AverageCost * item.Quantity;

                _db.InventoryTransactions.Add(
                    new InventoryTransaction(
                        inventoryItem.Id,
                        item.ProductVariantId,
                        item.Quantity,
                        InventoryTransactionType.AdjustmentIncrease,
                        order.Id.ToString(),
                        "Order cancellation"));
            }

            // Get accounts
            var arAccount = await _db.Accounts.FirstAsync(a => a.Code == "1200");
            var revenueAccount = await _db.Accounts.FirstAsync(a => a.Code == "4000");
            var inventoryAccount = await _db.Accounts.FirstAsync(a => a.Code == "1100");
            var cogsAccount = await _db.Accounts.FirstAsync(a => a.Code == "5000");
            var cashAccount = await _db.Accounts.FirstAsync(a => a.Code == "1000");

            // Reverse sale entry
            await _accountingService.CreateJournalEntryAsync(
                order.Id.ToString(),
                $"Cancellation Order {order.OrderNumber}",
                new List<(Guid, decimal, decimal)>
                {
            (revenueAccount.Id, order.TotalAmount, 0),
            (arAccount.Id, 0, order.TotalAmount),

            (inventoryAccount.Id, totalCogs, 0),
            (cogsAccount.Id, 0, totalCogs)
                });

            // Reverse payments if exist
            foreach (var payment in order.Payments)
            {
                await _accountingService.CreateJournalEntryAsync(
                    order.Id.ToString(),
                    $"Payment reversal for Order {order.OrderNumber}",
                    new List<(Guid, decimal, decimal)>
                    {
                (arAccount.Id, payment.Amount, 0),
                (cashAccount.Id, 0, payment.Amount)
                    });

                order.RegisterPayment(-payment.Amount);
            }

            order.Cancel();

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        public async Task<Guid> CreateDraftAsync(CreateOrderRequest request)
        {
            var userId = _currentUser.UserId;

            var customer = await _db.Customers
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (customer == null)
                throw new Exception("Customer not found.");

            var orderNumber = $"ORD-{DateTime.UtcNow.Ticks}";

            var order = new Order(orderNumber, customer.Id);

            foreach (var itemReq in request.Items)
            {
                var variant = await _db.ProductVariants
                    .FirstAsync(v => v.Id == itemReq.ProductVariantId);

                var item = new OrderItem(
                    order.Id,
                    variant.Id,
                    variant.SKU,
                    variant.Price,
                    itemReq.Quantity);

                order.AddItem(item);
            }

            _db.Orders.Add(order);

            await _db.SaveChangesAsync();

            return order.Id;
        }
        public async Task UpdateDraftAsync(Guid orderId, CreateOrderRequest request)
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                throw new Exception("Order not found.");

            order.ClearItems();

            foreach (var itemReq in request.Items)
            {
                var variant = await _db.ProductVariants
                    .FirstAsync(v => v.Id == itemReq.ProductVariantId);

                var item = new OrderItem(
                    order.Id,
                    variant.Id,
                    variant.SKU,
                    variant.Price,
                    itemReq.Quantity);

                order.AddItem(item);
            }

            await _db.SaveChangesAsync();
        }
    }
}
