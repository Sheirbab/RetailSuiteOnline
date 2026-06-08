using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Email;
using RetailSuite.Infrastructure.Modules.Customer.Services;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Locations.Entities;
using RetailSuite.Infrastructure.Modules.Orders.Dtos;
using RetailSuite.Infrastructure.Modules.Orders.Services;
using RetailSuite.Infrastructure.Modules.Tax.Services;
using RetailSuite.Modules.Accounting.Entities;
using RetailSuite.Modules.Accounting.Services;
using RetailSuite.Modules.Catalog.Entities;
using RetailSuite.Shared;

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

    /// <summary>
    /// Builds a SaleService with real RetailDbContext + real StoreCredit/Loyalty services,
    /// since they all share the same in-memory db. Email is a no-op. Current user context
    /// is a stub returning the supplied tenant + a fixed cashier id.
    /// </summary>
    private static SaleService NewSaleService(RetailDbContext db, Guid tenantId)
    {
        var currentUser = new Mock<ICurrentUserContext>();
        currentUser.Setup(x => x.TenantId).Returns(tenantId);
        currentUser.Setup(x => x.UserId).Returns(Guid.NewGuid());

        var storeCredit = new StoreCreditService(db, NullLogger<StoreCreditService>.Instance);
        var loyalty     = new LoyaltyService(db, NullLogger<LoyaltyService>.Instance);

        var invoiceStamper = new InvoiceStampingService(db, new SalesInvoiceNumberGenerator(db));

        return new SaleService(
            db,
            new AccountingService(db),
            new NoopEmailService(),
            storeCredit,
            loyalty,
            currentUser.Object,
            invoiceStamper);
    }

    [Fact]
    public async Task ProcessPosSaleAsync_DecrementsInventoryAndVariantStock()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryDb(tenantId);

        var location = new Location(tenantId, code: "MAIN", name: "Main Branch", isDefault: true);
        db.Locations.Add(location);
        await db.SaveChangesAsync();

        var product = new Product("Test Product", null);
        var variant = new ProductVariant(product.Id, "SKU-SALE", 100m);
        variant.StockQuantity = 5;

        db.Products.Add(product);
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        var inventoryItem = new InventoryItem(variant.Id, location.Id);
        inventoryItem.ReceiveStock(5, 40m);
        db.InventoryItems.Add(inventoryItem);
        db.Accounts.AddRange(
            new Account("1000", "Cash", AccountType.Asset),
            new Account("4000", "Revenue", AccountType.Revenue),
            new Account("1100", "Inventory", AccountType.Asset),
            new Account("5000", "Cost of Goods Sold", AccountType.Expense));
        await db.SaveChangesAsync();

        var service = NewSaleService(db, tenantId);

        await service.ProcessPosSaleAsync(new CreatePosSaleRequest
        {
            PaidAmount = 200m,
            Items =
            {
                new CreatePosSaleLine
                {
                    ProductVariantId = variant.Id,
                    Quantity         = 2
                }
            }
        });

        var updatedInventory = await db.InventoryItems.SingleAsync(i => i.ProductVariantId == variant.Id);
        var updatedVariant   = await db.ProductVariants.SingleAsync(v => v.Id == variant.Id);

        Assert.Equal(3, updatedInventory.CurrentStock);
        Assert.Equal(3, updatedVariant.StockQuantity);
    }

    private sealed class NoopEmailService : IEmailService
    {
        public Task SendAsync(string to, string subject, string htmlBody) => Task.CompletedTask;
    }
}
