using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Email;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Orders.Dtos;
using RetailSuite.Infrastructure.Modules.Orders.Services;
using RetailSuite.Modules.Accounting.Entities;
using RetailSuite.Modules.Accounting.Services;
using RetailSuite.Modules.Catalog.Entities;
using RetailSuite.Modules.Orders.Dtos;
using RetailSuite.Shared;
using Moq;

namespace RetailSuite.Tests.Unit;

public class SaleServiceTests
{
    private static RetailDbContext CreateInMemoryDb(Guid tenantId)
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<RetailDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new RetailDbContext(options, tenantContext.Object);
    }

    [Fact]
    public async Task ProcessPosSaleAsync_DecrementsInventoryAndVariantStock()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryDb(tenantId);

        var product = new Product("Test Product", null);
        var variant = new ProductVariant(product.Id, "SKU-SALE", 100m);
        variant.StockQuantity = 5;

        db.Products.Add(product);
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        var inventoryItem = new InventoryItem(variant.Id);
        inventoryItem.ReceiveStock(5, 40m);
        db.InventoryItems.Add(inventoryItem);
        db.Accounts.AddRange(
            new Account("1000", "Cash", AccountType.Asset),
            new Account("4000", "Revenue", AccountType.Revenue),
            new Account("1100", "Inventory", AccountType.Asset),
            new Account("5000", "Cost of Goods Sold", AccountType.Expense));
        await db.SaveChangesAsync();

        var service = new SaleService(
            db,
            new AccountingService(db),
            new NoopEmailService());

        await service.ProcessPosSaleAsync(new CreatePosSaleRequest
        {
            PaidAmount = 200m,
            Items =
            {
                new CreateOrderItemRequest
                {
                    ProductVariantId = variant.Id,
                    Quantity = 2
                }
            }
        });

        var updatedInventory = await db.InventoryItems.SingleAsync(i => i.ProductVariantId == variant.Id);
        var updatedVariant = await db.ProductVariants.SingleAsync(v => v.Id == variant.Id);

        Assert.Equal(3, updatedInventory.CurrentStock);
        Assert.Equal(3, updatedVariant.StockQuantity);
    }

    private sealed class NoopEmailService : IEmailService
    {
        public Task SendAsync(string to, string subject, string htmlBody) => Task.CompletedTask;
    }
}
