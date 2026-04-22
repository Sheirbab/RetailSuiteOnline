namespace RetailSuite.Infrastructure.Modules.Orders.Dtos;

public class ReturnOrderRequest
{
    /// <summary>Line items to return. Omit to return the entire order.</summary>
    public List<ReturnLineItem> Items { get; set; } = new();
}

public class ReturnLineItem
{
    public Guid ProductVariantId { get; set; }
    public int  Quantity         { get; set; }
}
