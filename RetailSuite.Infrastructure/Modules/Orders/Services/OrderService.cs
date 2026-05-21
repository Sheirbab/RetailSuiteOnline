using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure.Email;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Inventory.Services;
using RetailSuite.Infrastructure.Modules.Orders.Dtos;
using RetailSuite.Infrastructure.Payments;
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
        private readonly INotificationService _notifications;
        private readonly IPaymentGatewayFactory _gatewayFactory;
        private readonly ILogger<OrderService> _logger;

        /// <summary>Methods that don't go through a gateway — refunds are handled out of band.</summary>
        private static readonly HashSet<string> ManualRefundMethods =
            new(StringComparer.OrdinalIgnoreCase) { "Cash", "BankTransfer", "Manual" };

        public OrderService(
        RetailDbContext db,
        InventoryService inventoryService,
        AccountingService accountingService,
        ICurrentUserContext currentUser,
        INotificationService notifications,
        IPaymentGatewayFactory gatewayFactory,
        ILogger<OrderService> logger)
        {
            _db = db;
            _inventoryService = inventoryService;
            _accountingService = accountingService;
            _currentUser = currentUser;
            _notifications = notifications;
            _gatewayFactory = gatewayFactory;
            _logger = logger;
        }

        // ---------------------------------------
        // Confirm Order
        // ---------------------------------------
        public async Task ConfirmOrderAsync(Guid orderId)
        {
            _logger.LogInformation("Order {OrderId} confirmed", orderId);

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

            // Best-effort notification — never throws.
            await _notifications.SendOrderConfirmedAsync(order.Id);
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

                // Best-effort notification — never throws.
                await _notifications.SendOrderCancelledAsync(order.Id);
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

            // Best-effort notification — never throws.
            await _notifications.SendOrderCancelledAsync(order.Id);
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
                throw new NotFoundException("Order", orderId);

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

        // ---------------------------------------
        // Process Return / Refund
        // ---------------------------------------
        public async Task<decimal> ProcessReturnAsync(Guid orderId, ReturnOrderRequest request)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();

            var order = await _db.Orders
                .Include(o => o.Items)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                throw new NotFoundException("Order", orderId);

            if (order.Status != OrderStatus.Completed && order.Status != OrderStatus.Confirmed)
                throw new BusinessRuleException("Only confirmed or completed orders can be returned.");

            // Build map of items to return (fall back to full order if no items specified)
            var returnLines = request.Items.Any()
                ? request.Items
                : order.Items.Select(i => new ReturnLineItem
                    { ProductVariantId = i.ProductVariantId, Quantity = i.Quantity }).ToList();

            decimal totalReturnValue = 0;
            decimal totalCogsRestored = 0;

            // Get accounts
            var cashAccount      = await _db.Accounts.FirstAsync(a => a.Code == "1000");
            var revenueAccount   = await _db.Accounts.FirstAsync(a => a.Code == "4000");
            var inventoryAccount = await _db.Accounts.FirstAsync(a => a.Code == "1100");
            var cogsAccount      = await _db.Accounts.FirstAsync(a => a.Code == "5000");

            foreach (var line in returnLines)
            {
                var orderItem = order.Items.FirstOrDefault(i => i.ProductVariantId == line.ProductVariantId);
                if (orderItem == null)
                    throw new BusinessRuleException($"Variant {line.ProductVariantId} was not in the original order.");

                if (line.Quantity <= 0 || line.Quantity > orderItem.Quantity)
                    throw new BusinessRuleException($"Invalid return quantity for variant {line.ProductVariantId}.");

                var inventoryItem = await _db.InventoryItems
                    .FirstAsync(i => i.ProductVariantId == line.ProductVariantId);

                var costRestored = inventoryItem.AverageCost * line.Quantity;

                // Restore stock
                inventoryItem.ReceiveStock(line.Quantity, inventoryItem.AverageCost);
                totalCogsRestored += costRestored;

                var lineValue = orderItem.UnitPrice * line.Quantity;
                totalReturnValue += lineValue;

                _db.InventoryTransactions.Add(new InventoryTransaction(
                    inventoryItem.Id,
                    line.ProductVariantId,
                    line.Quantity,
                    InventoryTransactionType.AdjustmentIncrease,
                    order.Id.ToString(),
                    $"Return for order {order.OrderNumber}"));
            }

            // Reversal journal entry: reverse revenue + COGS
            await _accountingService.CreateJournalEntryAsync(
                order.Id.ToString(),
                $"Return for Order {order.OrderNumber}",
                new List<(Guid, decimal, decimal)>
                {
                    (revenueAccount.Id,   totalReturnValue,  0),               // DR Revenue
                    (cashAccount.Id,      0,                 totalReturnValue), // CR Cash (refund)
                    (inventoryAccount.Id, totalCogsRestored, 0),               // DR Inventory
                    (cogsAccount.Id,      0,                 totalCogsRestored) // CR COGS
                });

            // Reduce paid amount
            order.ApplyReturn(totalReturnValue);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            // ---------------------------------------
            // Gateway refunds — best-effort, post-commit
            // ---------------------------------------
            // The ledger has already booked the refund; calling the gateway is what actually
            // moves money back to the customer. Run after the DB transaction so a gateway
            // hiccup doesn't roll back the inventory + accounting work we just did.
            await IssueGatewayRefundsAsync(order, totalReturnValue);

            // Best-effort notification — never throws.
            await _notifications.SendReturnProcessedAsync(order.Id, totalReturnValue);

            return totalReturnValue;
        }

        /// <summary>
        /// Distributes the refund amount across the order's successful payments,
        /// most recent first. Cash / bank-transfer / manual payments are skipped —
        /// those refunds are settled out of band by finance.
        /// </summary>
        private async Task IssueGatewayRefundsAsync(Order order, decimal totalRefund)
        {
            if (totalRefund <= 0) return;

            // Most-recent-first ordering — refund the latest payment first to mirror typical
            // gateway behaviour (Stripe / JC / EP all refund the original charge id).
            var candidates = order.Payments
                .Where(p => !string.IsNullOrWhiteSpace(p.TransactionReference))
                .Where(p => !ManualRefundMethods.Contains(p.PaymentMethod))
                .OrderByDescending(p => p.PaidAt)
                .ToList();

            if (candidates.Count == 0)
            {
                _logger.LogInformation(
                    "Order {OrderNumber}: refund of {Amount} not routed to a gateway — no gateway-tracked payment found. " +
                    "Manual reconciliation required.",
                    order.OrderNumber, totalRefund);
                return;
            }

            var remaining = totalRefund;

            foreach (var payment in candidates)
            {
                if (remaining <= 0) break;

                var slice = Math.Min(payment.Amount, remaining);

                try
                {
                    var gateway = _gatewayFactory.GetByName(payment.PaymentMethod);
                    var result  = await gateway.RefundAsync(payment.TransactionReference!, slice);

                    if (result.Success)
                    {
                        _logger.LogInformation(
                            "Refund issued: Order={OrderNumber}, Method={Method}, OriginalTxn={Txn}, RefundTxn={RefundTxn}, Amount={Amount}",
                            order.OrderNumber, payment.PaymentMethod,
                            payment.TransactionReference, result.TransactionId, slice);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Gateway refund failed (will require manual follow-up): Order={OrderNumber}, Method={Method}, OriginalTxn={Txn}, Reason={Reason}",
                            order.OrderNumber, payment.PaymentMethod,
                            payment.TransactionReference, result.Error);
                    }
                }
                catch (Exception ex)
                {
                    // Never re-throw — the customer-facing ledger refund is already committed.
                    _logger.LogError(ex,
                        "Gateway refund threw for Order {OrderNumber} (method={Method}, txn={Txn}). " +
                        "Inventory + ledger already reflect the return; refund must be settled manually.",
                        order.OrderNumber, payment.PaymentMethod, payment.TransactionReference);
                }

                remaining -= slice;
            }

            if (remaining > 0)
            {
                _logger.LogWarning(
                    "Order {OrderNumber}: refund residual of {Amount} could not be routed to a gateway " +
                    "(no further gateway-tracked payments). Manual reconciliation required for the residual.",
                    order.OrderNumber, remaining);
            }
        }
    }
}
