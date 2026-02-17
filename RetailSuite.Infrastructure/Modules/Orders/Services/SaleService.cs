using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Orders.Dtos;
using RetailSuite.Modules.Accounting.Services;
using RetailSuite.Modules.Orders.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RetailSuite.Infrastructure.Modules.Orders.Services
{
    public class SaleService
    {
        private readonly RetailDbContext _db;
        private readonly AccountingService _accountingService;

        public SaleService(
            RetailDbContext db,
            AccountingService accountingService)
        {
            _db = db;
            _accountingService = accountingService;
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
                    itemReq.Quantity);

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

            var cashAccount = await _db.Accounts.FirstAsync(a => a.Code == "1000");
            var revenueAccount = await _db.Accounts.FirstAsync(a => a.Code == "4000");
            var inventoryAccount = await _db.Accounts.FirstAsync(a => a.Code == "1100");
            var cogsAccount = await _db.Accounts.FirstAsync(a => a.Code == "5000");

            await _accountingService.CreateJournalEntryAsync(
                order.Id.ToString(),
                $"POS Sale {order.OrderNumber}",
                new List<(Guid, decimal, decimal)>
                {
                (cashAccount.Id, order.TotalAmount, 0),
                (revenueAccount.Id, 0, order.TotalAmount),
                (cogsAccount.Id, totalCogs, 0),
                (inventoryAccount.Id, 0, totalCogs)
                });

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return order.Id;
        }
    }
}
