using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Inventory.Services;
using RetailSuite.Modules.Catalog.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Tests.Unit;

public class InventoryServiceTests
{
    private static RetailDbContext CreateInMemoryDb(Guid tenantId)
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<RetailDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RetailDbContext(options, tenantContext.Object);
    }

    [Fact]
    public async Task ReceiveStockAsync_IncreasesStockOnce_AndSyncsVariantSnapshot()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryDb(tenantId);
        var service = new InventoryService(db, Mock.Of<ILogger<InventoryService>>());

        var product = new Product("Test Product", null);
        var variant = new ProductVariant(product.Id, "SKU-001", 100m);
        db.Products.Add(product);
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        await service.ReceiveStockAsync(variant.Id, 10, 25m, "PO-001");

        var inventoryItem = await db.InventoryItems.SingleAsync(i => i.ProductVariantId == variant.Id);
        var syncedVariant = await db.ProductVariants.SingleAsync(v => v.Id == variant.Id);

        Assert.Equal(10, inventoryItem.CurrentStock);
        Assert.Equal(10, syncedVariant.StockQuantity);
        Assert.Equal(25m, syncedVariant.AverageCost);
    }

    [Fact]
    public async Task AdjustStockAsync_SyncsVariantStockQuantity()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryDb(tenantId);
        var service = new InventoryService(db, Mock.Of<ILogger<InventoryService>>());

        var product = new Product("Test Product", null);
        var variant = new ProductVariant(product.Id, "SKU-002", 100m);
        db.Products.Add(product);
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        await service.AdjustStockAsync(
            variant.Id,
            5,
            InventoryTransactionType.AdjustmentIncrease,
            "ADJ-001",
            "Initial count");

        var inventoryItem = await db.InventoryItems.SingleAsync(i => i.ProductVariantId == variant.Id);
        var syncedVariant = await db.ProductVariants.SingleAsync(v => v.Id == variant.Id);

        Assert.Equal(5, inventoryItem.CurrentStock);
        Assert.Equal(5, syncedVariant.StockQuantity);
    }
}
