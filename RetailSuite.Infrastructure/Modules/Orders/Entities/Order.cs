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

    /// <summary>Order-level discount (in rupees) applied at the till — separate from per-line discounts.</summary>
    public decimal OrderDiscountAmount { get; private set; }

    /// <summary>Store-credit amount redeemed against this sale (denormalised — actual ledger is in StoreCreditTransactions).</summary>
    public decimal StoreCreditRedeemed { get; private set; }

    /// <summary>Loyalty rupees credited against this sale (1 point → PointValueRupees).</summary>
    public decimal LoyaltyRedeemedRupees { get; private set; }

    /// <summary>Loyalty points redeemed (not rupees) — kept for audit.</summary>
    public int LoyaltyPointsRedeemed { get; private set; }

    /// <summary>User who rang up this sale (POS cashier). Null for online / system sales.</summary>
    public Guid? CashierUserId { get; private set; }

    // ---- Online-store extensions (Sprint C) ----------------------------

    /// <summary>"POS", "Online" — origin of the sale. Defaults to POS for back-compat.</summary>
    public string Channel { get; private set; } = "POS";

    /// <summary>Selected shipping method code at checkout (e.g. "FLAT", "PICKUP").</summary>
    public string? ShippingMethodCode { get; private set; }

    /// <summary>Shipping fee added to TotalAmount.</summary>
    public decimal ShippingAmount { get; private set; }

    /// <summary>JSON snapshot of the shipping address captured at checkout (guest or registered).</summary>
    public string? ShippingAddressJson { get; private set; }

    /// <summary>Guest order: customer name typed at checkout. Null when CustomerId is a real customer.</summary>
    public string? GuestName { get; private set; }

    /// <summary>Guest order: phone number — also the order-tracking key for guests.</summary>
    public string? GuestPhone { get; private set; }

    /// <summary>Guest order: email — receipt + tracking link sent here.</summary>
    public string? GuestEmail { get; private set; }

    /// <summary>"Cash", "COD", "Stripe", "EasyPaisa", "JazzCash", "BankTransfer".</summary>
    public string? PaymentMethodCode { get; private set; }

    /// <summary>Fulfillment lifecycle: Pending / Shipped / Delivered / Cancelled. Distinct from Status (Order lifecycle).</summary>
    public string FulfillmentStatus { get; private set; } = "Pending";

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

    // ---- POS extensions (cash counter) ---------------------------------

    /// <summary>Stamp the cashier who's ringing up this sale.</summary>
    public void SetCashier(Guid userId) => CashierUserId = userId;

    /// <summary>
    /// Apply an order-level discount (in rupees). Reduces the running TotalAmount.
    /// Caller is responsible for validating the discount doesn't exceed the cart subtotal.
    /// </summary>
    public void ApplyOrderDiscount(decimal amount)
    {
        if (amount < 0) throw new BusinessRuleException("Discount cannot be negative.");
        if (Status != OrderStatus.Draft)
            throw new BusinessRuleException("Discount can only be applied while the order is in Draft.");
        if (amount > TotalAmount)
            throw new BusinessRuleException("Order discount cannot exceed the cart total.");

        OrderDiscountAmount = amount;
        TotalAmount -= amount;
    }

    /// <summary>
    /// Record that the customer redeemed store credit against this sale. Reduces the amount
    /// the cashier needs to collect in cash; the actual ledger entry is separate.
    /// </summary>
    public void ApplyStoreCreditRedemption(decimal amount)
    {
        if (amount < 0) throw new BusinessRuleException("Redemption cannot be negative.");
        if (amount > TotalAmount - StoreCreditRedeemed - LoyaltyRedeemedRupees)
            throw new BusinessRuleException("Redemption would exceed amount due.");
        StoreCreditRedeemed += amount;
    }

    /// <summary>
    /// Record loyalty redemption (points + their rupee equivalent). The cashier collects
    /// less cash by exactly the rupee value.
    /// </summary>
    public void ApplyLoyaltyRedemption(int points, decimal rupees)
    {
        if (points < 0 || rupees < 0) throw new BusinessRuleException("Loyalty redemption cannot be negative.");
        if (rupees > TotalAmount - StoreCreditRedeemed - LoyaltyRedeemedRupees)
            throw new BusinessRuleException("Loyalty redemption would exceed amount due.");
        LoyaltyPointsRedeemed += points;
        LoyaltyRedeemedRupees += rupees;
    }

    /// <summary>How much cash / card the customer still needs to pay after all redemptions.</summary>
    public decimal AmountDueAfterRedemptions =>
        Math.Max(0, TotalAmount - StoreCreditRedeemed - LoyaltyRedeemedRupees);

    // ---- Online-store mutators -----------------------------------------

    public void SetChannel(string channel) =>
        Channel = string.IsNullOrWhiteSpace(channel) ? "POS" : channel.Trim();

    public void SetGuestContact(string name, string phone, string? email)
    {
        GuestName  = name;
        GuestPhone = phone;
        GuestEmail = email;
    }

    public void SetShipping(string methodCode, decimal amount, string addressJson)
    {
        if (Status != OrderStatus.Draft)
            throw new BusinessRuleException("Shipping can only be set while order is Draft.");

        ShippingMethodCode  = methodCode;
        ShippingAmount      = Math.Max(0, amount);
        ShippingAddressJson = addressJson;

        // Shipping is part of the total the customer pays.
        TotalAmount += ShippingAmount;
    }

    public void SetPaymentMethod(string code) => PaymentMethodCode = code;

    public void MarkShipped()    => FulfillmentStatus = "Shipped";
    public void MarkDelivered()  => FulfillmentStatus = "Delivered";
    public void MarkUnfulfilled() => FulfillmentStatus = "Pending";

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