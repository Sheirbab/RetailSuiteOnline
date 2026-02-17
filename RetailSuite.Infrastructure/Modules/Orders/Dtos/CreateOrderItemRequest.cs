namespace RetailSuite.Modules.Orders.Dtos;

public class CreateOrderItemRequest
{
    public Guid ProductVariantId { get; set; }

    public int Quantity { get; set; }
}