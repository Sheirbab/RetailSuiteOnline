using RetailSuite.Infrastructure.Modules.Customer.Entities;
using RetailSuite.Modules.Accounting.Entities;
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

    private readonly List<Payment> _payments = new();
    public IReadOnlyCollection<Payment> Payments => _payments;

    private Order() { }

    public Order(string orderNumber, Guid customerId)
    {
        OrderNumber = orderNumber;
        CustomerId = customerId;
        Status = OrderStatus.Draft;
    }
    public void AddItem(OrderItem item)
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException("Only draft orders can be modified.");

        _items.Add(item);
        TotalAmount += item.LineTotal;
    }
    public void ClearItems()
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException("Only draft orders can be modified.");

        _items.Clear();
        TotalAmount = 0;
    }

    public void Confirm()
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException("Only draft orders can be confirmed.");

        Status = OrderStatus.Confirmed;
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("Already cancelled.");

        Status = OrderStatus.Cancelled;
    }

    public void RegisterPayment(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Invalid payment.");

        if (PaidAmount + amount > TotalAmount)
            throw new InvalidOperationException("Overpayment.");

        PaidAmount += amount;
    }
    public void Complete() => Status = OrderStatus.Completed;
}