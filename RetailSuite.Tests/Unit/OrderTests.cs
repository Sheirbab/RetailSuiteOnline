using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Modules.Orders.Entities;

namespace RetailSuite.Tests.Unit;

public class OrderTests
{
    private static Order CreateDraftOrder() => new Order("ORD-001", Guid.NewGuid());

    private static OrderItem CreateItem(decimal price = 100m, int qty = 1)
        => new OrderItem(Guid.NewGuid(), Guid.NewGuid(), "SKU-001", price, qty);

    // -----------------------------------------------------------------------
    // AddItem
    // -----------------------------------------------------------------------
    [Fact]
    public void AddItem_AccumulatesTotalAmount()
    {
        var order = CreateDraftOrder();
        order.AddItem(CreateItem(50m, 2));
        order.AddItem(CreateItem(30m, 1));
        Assert.Equal(130m, order.TotalAmount); // 2×50 + 1×30
    }

    [Fact]
    public void AddItem_ThrowsBusinessRuleException_WhenOrderIsConfirmed()
    {
        var order = CreateDraftOrder();
        order.AddItem(CreateItem());
        order.Confirm();
        Assert.Throws<BusinessRuleException>(() => order.AddItem(CreateItem()));
    }

    // -----------------------------------------------------------------------
    // Confirm
    // -----------------------------------------------------------------------
    [Fact]
    public void Confirm_ChangesStatusToConfirmed()
    {
        var order = CreateDraftOrder();
        order.AddItem(CreateItem());
        order.Confirm();
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void Confirm_ThrowsBusinessRuleException_WhenAlreadyConfirmed()
    {
        var order = CreateDraftOrder();
        order.AddItem(CreateItem());
        order.Confirm();
        Assert.Throws<BusinessRuleException>(() => order.Confirm());
    }

    // -----------------------------------------------------------------------
    // Cancel
    // -----------------------------------------------------------------------
    [Fact]
    public void Cancel_ChangesStatusToCancelled()
    {
        var order = CreateDraftOrder();
        order.Cancel();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cancel_ThrowsBusinessRuleException_WhenAlreadyCancelled()
    {
        var order = CreateDraftOrder();
        order.Cancel();
        Assert.Throws<BusinessRuleException>(() => order.Cancel());
    }

    // -----------------------------------------------------------------------
    // RegisterPayment
    // -----------------------------------------------------------------------
    [Fact]
    public void RegisterPayment_IncreasesPaidAmount()
    {
        var order = CreateDraftOrder();
        order.AddItem(CreateItem(200m));
        order.RegisterPayment(100m);
        Assert.Equal(100m, order.PaidAmount);
        Assert.Equal(100m, order.OutstandingAmount);
    }

    [Fact]
    public void RegisterPayment_ThrowsBusinessRuleException_WhenOverpaying()
    {
        var order = CreateDraftOrder();
        order.AddItem(CreateItem(100m));
        Assert.Throws<BusinessRuleException>(() => order.RegisterPayment(150m));
    }

    // -----------------------------------------------------------------------
    // Tax
    // -----------------------------------------------------------------------
    [Fact]
    public void AddItem_AccumulatesTaxAmount()
    {
        var order = CreateDraftOrder();
        var item = new OrderItem(Guid.NewGuid(), Guid.NewGuid(), "SKU-TAX", 100m, 1, 0.17m);
        order.AddItem(item);
        Assert.Equal(17m, order.TaxAmount);
    }
}
