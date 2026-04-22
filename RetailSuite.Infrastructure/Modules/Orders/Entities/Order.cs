using RetailSuite.Infrastructure.Exceptions;
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
    /// <summary>Total tax charged across all items. Populated when the order is confirmed/completed.</summary>
    public decimal TaxAmount { get; private set; }

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
            throw new BusinessRuleException("Only draft orders can be modified.");

        _items.Add(item);
        TotalAmount += item.LineTotal;
        TaxAmount   += item.LineTaxAmount;
    }
    public void ClearItems()
    {
        if (Status != OrderStatus.Draft)
            throw new BusinessRuleException("Only draft orders can be modified.");

        _items.Clear();
        TotalAmount = 0;
        TaxAmount   = 0;
    }

    public void Confirm()
    {
        if (Status != OrderStatus.Draft)
            throw new BusinessRuleException("Only draft orders can be confirmed.");

        Status = OrderStatus.Confirmed;
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Cancelled)
            throw new BusinessRuleException("Order is already cancelled.");

        Status = OrderStatus.Cancelled;
    }

    public void RegisterPayment(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Invalid payment.");

        if (PaidAmount + amount > TotalAmount)
            throw new BusinessRuleException("Payment would exceed the order total.");

        PaidAmount += amount;
    }
    public void Complete() => Status = OrderStatus.Completed;

    /// <summary>Records a partial/full return, reducing the paid amount accordingly.</summary>
    public void ApplyReturn(decimal returnAmount)
    {
        if (returnAmount <= 0)
            throw new BusinessRuleException("Return amount must be positive.");

        if (returnAmount > PaidAmount)
            throw new BusinessRuleException("Return amount exceeds the amount paid.");

        PaidAmount -= returnAmount;
    }
}