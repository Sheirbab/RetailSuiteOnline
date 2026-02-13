using RetailSuite.Shared;

namespace RetailSuite.Modules.Orders.Entities;

public class OrderItem : TenantEntity
{
    public Guid OrderId { get; private set; }

    public Guid ProductVariantId { get; private set; }

    public string SKU { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    public decimal LineTotal => UnitPrice * Quantity;

    private OrderItem() { }

    public OrderItem(
        Guid orderId,
        Guid productVariantId,
        string sku,
        decimal unitPrice,
        int quantity)
    {
        OrderId = orderId;
        ProductVariantId = productVariantId;
        SKU = sku;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }
}