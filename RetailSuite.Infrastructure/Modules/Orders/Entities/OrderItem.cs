using RetailSuite.Shared;

namespace RetailSuite.Modules.Orders.Entities;

public class OrderItem : TenantEntity
{
    public Guid OrderId { get; private set; }

    public Guid ProductVariantId { get; private set; }

    public string SKU { get; private set; } = string.Empty;

    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>Tax rate at time of sale (fraction, e.g. 0.17 = 17%). Captured from variant.</summary>
    public decimal TaxRate { get; private set; } = 0;

    /// <summary>Per-line discount in rupees applied at POS (e.g. "Rs 50 off this jacket").</summary>
    public decimal LineDiscountAmount { get; private set; }

    /// <summary>Net line value before tax — quantity × unit price minus line discount.</summary>
    public decimal LineNet => (UnitPrice * Quantity) - LineDiscountAmount;

    /// <summary>Gross line value before tax (no discount applied) — useful for receipts.</summary>
    public decimal LineGross => UnitPrice * Quantity;

    /// <summary>Total charge for this line — discounted net + tax-on-net.</summary>
    public decimal LineTotal => LineNet + LineTaxAmount;

    /// <summary>Tax computed on the discounted net (PK practice).</summary>
    public decimal LineTaxAmount => LineNet * TaxRate;

    private OrderItem() { }

    public OrderItem(
        Guid orderId,
        Guid productVariantId,
        string sku,
        decimal unitPrice,
        int quantity,
        decimal taxRate = 0,
        decimal lineDiscountAmount = 0)
    {
        OrderId = orderId;
        ProductVariantId = productVariantId;
        SKU = sku;
        UnitPrice = unitPrice;
        Quantity = quantity;
        TaxRate = taxRate;
        LineDiscountAmount = lineDiscountAmount;
    }

    /// <summary>Replace the line discount in rupees. Caller ensures it doesn't exceed line gross.</summary>
    public void SetLineDiscount(decimal discount)
    {
        if (discount < 0) discount = 0;
        if (discount > LineGross) discount = LineGross;
        LineDiscountAmount = discount;
    }
}
