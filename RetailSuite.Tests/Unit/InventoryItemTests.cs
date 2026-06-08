using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;

namespace RetailSuite.Tests.Unit;

public class InventoryItemTests
{
    // -----------------------------------------------------------------------
    // ReceiveStock
    // -----------------------------------------------------------------------
    [Fact]
    public void ReceiveStock_IncreasesCurrentStock()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid());
        item.ReceiveStock(10, 50m);
        Assert.Equal(10, item.CurrentStock);
    }

    [Fact]
    public void ReceiveStock_CalculatesWeightedAverageCost()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid());
        item.ReceiveStock(10, 50m);  // avg = 50
        item.ReceiveStock(10, 70m);  // avg = (500+700)/20 = 60
        Assert.Equal(60m, item.AverageCost);
    }

    [Fact]
    public void ReceiveStock_ThrowsOnZeroQuantity()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => item.ReceiveStock(0, 10m));
    }

    // -----------------------------------------------------------------------
    // IssueStock
    // -----------------------------------------------------------------------
    [Fact]
    public void IssueStock_DecreasesCurrentStock()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid());
        item.ReceiveStock(20, 100m);
        item.IssueStock(5);
        Assert.Equal(15, item.CurrentStock);
    }

    [Fact]
    public void IssueStock_ReturnsCorrectCogs()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid());
        item.ReceiveStock(10, 80m);   // avg cost = 80
        var cogs = item.IssueStock(3);
        Assert.Equal(240m, cogs);     // 3 × 80 = 240
    }

    [Fact]
    public void IssueStock_ThrowsBusinessRuleException_WhenInsufficientStock()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid());
        item.ReceiveStock(2, 50m);
        Assert.Throws<BusinessRuleException>(() => item.IssueStock(5));
    }

    [Fact]
    public void IssueStock_ResetsAverageCost_WhenStockReachesZero()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid());
        item.ReceiveStock(5, 100m);
        item.IssueStock(5);
        Assert.Equal(0m, item.AverageCost);
    }
}
