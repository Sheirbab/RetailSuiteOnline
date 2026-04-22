using RetailSuite.Shared;

namespace RetailSuite.Modules.Orders.Entities;

public class OrderItem : TenantEntity
{
    public Guid OrderId { get; private set; }

    public Guid ProductVariantId { get; private set; }

    public string SKU { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>Tax rate at time of sale (fraction, e.g. 0.17 = 17%). Captured from variant.</summary>
    public decimal TaxRate { get; private set; } = 0;

    public decimal LineTotal    => UnitPrice * Quantity;
    public decimal LineTaxAmount => LineTotal * TaxRate;

    private OrderItem() { }

    public OrderItem(
        Guid orderId,
        Guid productVariantId,
        string sku,
        decimal unitPrice,
        int quantity,
        decimal taxRate = 0)
    {
        OrderId = orderId;
        ProductVariantId = productVariantId;
        SKU = sku;
        UnitPrice = unitPrice;
        Quantity = quantity;
        TaxRate = taxRate;
    }
}