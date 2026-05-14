using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Receiving.Entities;

namespace RetailSuite.Tests.Unit;

/// <summary>
/// Domain tests for the ReceivingOrder + ReceivingOrderItem state machine.
/// These are pure entity tests — no DB.
/// </summary>
public class ReceivingOrderTests
{
    private static ReceivingOrder NewDraftWithLine(int expectedQty = 10, decimal unitCost = 50m)
    {
        var tenantId = Guid.NewGuid();
        var order    = new ReceivingOrder(tenantId, "PO-202605-0001", supplierId: Guid.NewGuid());
        var line     = new ReceivingOrderItem(
            tenantId, order.Id, Guid.NewGuid(), "SKU-X", expectedQty, unitCost);
        order.AddItem(line);
        return order;
    }

    [Fact]
    public void NewOrder_StartsAsDraft()
    {
        var order = NewDraftWithLine();
        Assert.Equal(ReceivingStatus.Draft, order.Status);
        Assert.Equal(500m, order.ExpectedTotal);   // 10 * 50
        Assert.Equal(0m,   order.ReceivedTotal);
    }

    [Fact]
    public void Submit_DraftMovesToOpen()
    {
        var order = NewDraftWithLine();
        order.Submit();
        Assert.Equal(ReceivingStatus.Open, order.Status);
        Assert.NotNull(order.SubmittedAt);
    }

    [Fact]
    public void Submit_RejectsEmptyOrder()
    {
        var order = new ReceivingOrder(Guid.NewGuid(), "PO-X", null);
        Assert.Throws<BusinessRuleException>(() => order.Submit());
    }

    [Fact]
    public void AddItem_RejectedOnceSubmitted()
    {
        var order = NewDraftWithLine();
        order.Submit();

        var extra = new ReceivingOrderItem(order.TenantId, order.Id, Guid.NewGuid(), "SKU-Y", 5, 20m);
        Assert.Throws<BusinessRuleException>(() => order.AddItem(extra));
    }

    [Fact]
    public void RecordReceipt_PartialQuantity_MovesToPartiallyReceived()
    {
        var order = NewDraftWithLine(expectedQty: 10);
        order.Submit();

        var lineId = order.Items.First().Id;
        order.RecordReceipt(lineId, 4);

        Assert.Equal(ReceivingStatus.PartiallyReceived, order.Status);
        Assert.Equal(4, order.Items.First().ReceivedQuantity);
        Assert.Equal(6, order.Items.First().OutstandingQuantity);
        Assert.Equal(ReceivingLineStatus.PartiallyReceived, order.Items.First().Status);
    }

    [Fact]
    public void RecordReceipt_FullQuantity_ClosesOrderAutomatically()
    {
        var order = NewDraftWithLine(expectedQty: 10);
        order.Submit();

        var lineId = order.Items.First().Id;
        order.RecordReceipt(lineId, 10);

        Assert.Equal(ReceivingStatus.Closed, order.Status);
        Assert.NotNull(order.ClosedAt);
        Assert.Equal(ReceivingLineStatus.Received, order.Items.First().Status);
    }

    [Fact]
    public void RecordReceipt_ExceedingExpected_Rejected()
    {
        var order = NewDraftWithLine(expectedQty: 10);
        order.Submit();

        var lineId = order.Items.First().Id;
        Assert.Throws<BusinessRuleException>(() => order.RecordReceipt(lineId, 11));
    }

    [Fact]
    public void RecordReceipt_OnDraftOrder_Rejected()
    {
        var order = NewDraftWithLine();
        var lineId = order.Items.First().Id;
        Assert.Throws<BusinessRuleException>(() => order.RecordReceipt(lineId, 1));
    }

    [Fact]
    public void Cancel_FromOpen_Works()
    {
        var order = NewDraftWithLine();
        order.Submit();
        order.Cancel();
        Assert.Equal(ReceivingStatus.Cancelled, order.Status);
        Assert.NotNull(order.CancelledAt);
    }

    [Fact]
    public void Cancel_RejectedAfterClose()
    {
        var order = NewDraftWithLine(expectedQty: 1);
        order.Submit();
        order.RecordReceipt(order.Items.First().Id, 1);   // auto-closes
        Assert.Throws<BusinessRuleException>(() => order.Cancel());
    }

    [Fact]
    public void Close_ForcesClosed_EvenIfShort()
    {
        // Sometimes the supplier confirms they won't deliver the remainder.
        var order = NewDraftWithLine(expectedQty: 10);
        order.Submit();
        order.RecordReceipt(order.Items.First().Id, 3);
        order.Close();
        Assert.Equal(ReceivingStatus.Closed, order.Status);
    }
}
