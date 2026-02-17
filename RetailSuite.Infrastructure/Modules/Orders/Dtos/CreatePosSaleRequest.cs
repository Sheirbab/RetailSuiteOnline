using RetailSuite.Modules.Orders.Dtos;

namespace RetailSuite.Infrastructure.Modules.Orders.Dtos
{
    public class CreatePosSaleRequest
    {
        public Guid? CustomerId { get; set; } // optional for walk-in
        public List<CreateOrderItemRequest> Items { get; set; } = new();
        public decimal PaidAmount { get; set; }
    }
}
