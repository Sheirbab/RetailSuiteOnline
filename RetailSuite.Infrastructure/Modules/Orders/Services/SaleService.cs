using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Orders.Dtos;
using RetailSuite.Infrastructure.Email;
using RetailSuite.Modules.Accounting.Services;
using RetailSuite.Modules.Orders.Entities;

namespace RetailSuite.Infrastructure.Modules.Orders.Services
{
    public class SaleService
    {
        private readonly RetailDbContext _db;
        private readonly AccountingService _accountingService;
        private readonly IEmailService _emailService;

        public SaleService(
            RetailDbContext db,
            AccountingService accountingService,
            IEmailService emailService)
        {
            _db = db;
            _accountingService = accountingService;
            _emailService = emailService;
        }

        public async Task<Guid> ProcessPosSaleAsync(CreatePosSaleRequest request)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();

            var orderNumber = $"POS-{DateTime.UtcNow.Ticks}";
            var customerId = request.CustomerId ?? Guid.Empty;

            var order = new Order(orderNumber, customerId);

            decimal totalCogs = 0;

            foreach (var itemReq in request.Items)
            {
                var variant = await _db.ProductVariants
                    .FirstAsync(v => v.Id == itemReq.ProductVariantId);

                var inventoryItem = await _db.InventoryItems
                    .FirstAsync(i => i.ProductVariantId == variant.Id);

                var costAmount = inventoryItem.IssueStock(itemReq.Quantity);
                totalCogs += costAmount;

                var orderItem = new OrderItem(
                    order.Id,
                    variant.Id,
                    variant.SKU,
                    variant.Price,
                    itemReq.Quantity,
                    variant.TaxRate);

                order.AddItem(orderItem);

                _db.InventoryTransactions.Add(
                    new InventoryTransaction(
                        inventoryItem.Id,
                        variant.Id,
                        -itemReq.Quantity,
                        InventoryTransactionType.Sale,
                        order.Id.ToString(),
                        "POS sale"));
            }

            order.Confirm();
            order.Complete();

            _db.Orders.Add(order);

            // Load accounts
            var cashAccount      = await _db.Accounts.FirstAsync(a => a.Code == "1000");
            var revenueAccount   = await _db.Accounts.FirstAsync(a => a.Code == "4000");
            var inventoryAccount = await _db.Accounts.FirstAsync(a => a.Code == "1100");
            var cogsAccount      = await _db.Accounts.FirstAsync(a => a.Code == "5000");

            var journalLines = new List<(Guid, decimal, decimal)>
            {
                (cashAccount.Id,      order.TotalAmount, 0),
                (revenueAccount.Id,   0, order.TotalAmount - order.TaxAmount),
                (cogsAccount.Id,      totalCogs, 0),
                (inventoryAccount.Id, 0, totalCogs)
            };

            // Add tax payable lines if there is tax
            if (order.TaxAmount > 0)
            {
                var taxAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "2000");
                if (taxAccount != null)
                {
                    // Revenue is already reduced above; credit Tax Payable
                    journalLines.Add((taxAccount.Id, 0, order.TaxAmount));
                }
            }

            await _accountingService.CreateJournalEntryAsync(
                order.Id.ToString(),
                $"POS Sale {order.OrderNumber}",
                journalLines);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            // Send receipt email (fire-and-forget — never breaks the sale)
            if (customerId != Guid.Empty)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId);
                        if (customer?.Email != null)
                        {
                            var body = $@"
<h2>Receipt — {order.OrderNumber}</h2>
<p>Thank you for your purchase!</p>
<p><strong>Total:</strong> Rs {order.TotalAmount:N2}</p>
<p><strong>Tax:</strong> Rs {order.TaxAmount:N2}</p>
<p><strong>Date:</strong> {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC</p>";
                            await _emailService.SendAsync(customer.Email, $"Receipt: {order.OrderNumber}", body);
                        }
                    }
                    catch { /* email failure must not affect sale */ }
                });
            }

            return order.Id;
        }
    }
}
