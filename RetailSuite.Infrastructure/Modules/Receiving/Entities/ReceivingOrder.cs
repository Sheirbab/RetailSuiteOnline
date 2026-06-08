using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Receiving.Entities;

/// <summary>
/// A purchase / receiving order placed against a supplier.
/// Lifecycle: Draft → Open → PartiallyReceived → Closed (or Cancelled at any point before Closed).
/// Once committed via <see cref="Submit"/>, the lines cannot be edited; inventory is only
/// affected when <see cref="ReceivingOrderService"/> records goods received.
/// </summary>
public class ReceivingOrder : TenantEntity
{
    public Guid? SupplierId { get; private set; }

    /// <summary>Human-readable order number (e.g. "PO-202605-0001"). Unique per tenant.</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>Optional external PO reference from the supplier's system.</summary>
    public string? SupplierReference { get; private set; }

    public ReceivingStatus Status { get; private set; } = ReceivingStatus.Draft;

    public DateTime? ExpectedDate { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Sum of (UnitCost × ExpectedQuantity) across all lines.</summary>
    public decimal ExpectedTotal { get; private set; }

    /// <summary>Sum of (UnitCost × ReceivedQuantity) across all lines.</summary>
    public decimal ReceivedTotal { get; private set; }

    public string Currency { get; private set; } = "PKR";

    /// <summary>The branch / shop these goods are being received into. Stamped at create.</summary>
    public Guid? DestinationLocationId { get; private set; }

    private readonly List<ReceivingOrderItem> _items = new();
    public IReadOnlyCollection<ReceivingOrderItem> Items => _items;

    private ReceivingOrder() { }

    public ReceivingOrder(Guid tenantId, string orderNumber, Guid? supplierId, string currency = "PKR")
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            throw new ArgumentException("OrderNumber is required.", nameof(orderNumber));

        Id          = Guid.NewGuid();
        CreatedAt   = DateTime.UtcNow;
        TenantId    = tenantId;
        OrderNumber = orderNumber;
        SupplierId  = supplierId;
        Currency    = string.IsNullOrWhiteSpace(currency) ? "PKR" : currency.ToUpperInvariant();
    }

    public void SetExpectedDate(DateTime? date) => ExpectedDate = date;
    public void SetSupplierReference(string? r) => SupplierReference = r;
    public void SetNotes(string? notes)         => Notes = notes;

    /// <summary>Set the destination branch for these goods. Can only be set while the order is in Draft.</summary>
    public void SetDestinationLocation(Guid locationId)
    {
        if (Status != ReceivingStatus.Draft)
            throw new BusinessRuleException("Destination location can only be set while the order is in Draft.");
        if (locationId == Guid.Empty)
            throw new ArgumentException("LocationId is required.", nameof(locationId));
        DestinationLocationId = locationId;
    }

    public void AddItem(ReceivingOrderItem item)
    {
        if (Status != ReceivingStatus.Draft)
            throw new BusinessRuleException("Only Draft orders can be modified.");
        if (item == null) throw new ArgumentNullException(nameof(item));

        _items.Add(item);
        RecalculateTotals();
    }

    public void RemoveItem(Guid itemId)
    {
        if (Status != ReceivingStatus.Draft)
            throw new BusinessRuleException("Only Draft orders can be modified.");

        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item == null) return;

        _items.Remove(item);
        RecalculateTotals();
    }

    public void Submit()
    {
        if (Status != ReceivingStatus.Draft)
            throw new BusinessRuleException("Only Draft orders can be submitted.");
        if (_items.Count == 0)
            throw new BusinessRuleException("Cannot submit a receiving order with no items.");

        Status      = ReceivingStatus.Open;
        SubmittedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Apply a receipt against an existing line. Caller is responsible for the inventory side-effect.
    /// Updates the line's received quantity, the order totals, and the order's overall status.
    /// </summary>
    public void RecordReceipt(Guid itemId, int receivedNow)
    {
        if (Status != ReceivingStatus.Open && Status != ReceivingStatus.PartiallyReceived)
            throw new BusinessRuleException("Order must be Open or PartiallyReceived to receive items.");
        if (receivedNow <= 0)
            throw new BusinessRuleException("Received quantity must be positive.");

        var line = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new NotFoundException("ReceivingOrderItem", itemId);

        line.AddReceipt(receivedNow);
        RecalculateTotals();
        RecalculateStatus();
    }

    /// <summary>Force the order to Closed even if some lines are short — used when supplier confirms no more deliveries.</summary>
    public void Close()
    {
        if (Status == ReceivingStatus.Closed)    return;
        if (Status == ReceivingStatus.Cancelled) throw new BusinessRuleException("Cancelled orders cannot be closed.");

        Status   = ReceivingStatus.Closed;
        ClosedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (Status == ReceivingStatus.Closed)
            throw new BusinessRuleException("Closed orders cannot be cancelled.");

        Status      = ReceivingStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
    }

    // ---------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------

    private void RecalculateTotals()
    {
        ExpectedTotal = _items.Sum(i => i.UnitCost * i.ExpectedQuantity);
        ReceivedTotal = _items.Sum(i => i.UnitCost * i.ReceivedQuantity);
    }

    private void RecalculateStatus()
    {
        if (Status == ReceivingStatus.Cancelled || Status == ReceivingStatus.Closed)
            return;

        var anyReceived = _items.Any(i => i.ReceivedQuantity > 0);
        var allFull     = _items.All(i => i.ReceivedQuantity >= i.ExpectedQuantity);

        if (allFull)
        {
            Status   = ReceivingStatus.Closed;
            ClosedAt = DateTime.UtcNow;
        }
        else if (anyReceived)
        {
            Status = ReceivingStatus.PartiallyReceived;
        }
    }
}
