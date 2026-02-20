using RetailSuite.Infrastructure.Modules.Customer.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Modules.Orders.Entities;
public class Order : TenantEntity
{
    public string OrderNumber { get; private set; }

    public Guid CustomerId { get; private set; }
    public Customer Customer { get; private set; }

    public OrderStatus Status { get; private set; }

    public decimal TotalAmount { get; private set; }
    public decimal PaidAmount { get; private set; }

    public decimal OutstandingAmount => TotalAmount - PaidAmount;

    public bool IsFullyPaid => OutstandingAmount <= 0;

    private readonly List<OrderItem> _items = new();
    public IReadOnlyCollection<OrderItem> Items => _items;

    private Order() { }

    public Order(string orderNumber, Guid customerId)
    {
        OrderNumber = orderNumber;
        CustomerId = customerId;
        Status = OrderStatus.Draft;
    }

    public void AddItem(OrderItem item)
    {
        _items.Add(item);
        TotalAmount += item.LineTotal;
    }

    public void Confirm() => Status = OrderStatus.Confirmed;
    public void Complete() => Status = OrderStatus.Completed;
    public void Cancel() => Status = OrderStatus.Cancelled;
    public void RegisterPayment(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Payment amount must be positive.");

        if (PaidAmount + amount > TotalAmount)
            throw new InvalidOperationException("Overpayment not allowed.");

        PaidAmount += amount;
    }
    public void UpdateItems(List<OrderItem> newItems)
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException("Only draft orders can be edited.");

        _items.Clear();
        TotalAmount = 0;

        foreach (var item in newItems)
        {
            _items.Add(item);
            TotalAmount += item.LineTotal;
        }
    }
}