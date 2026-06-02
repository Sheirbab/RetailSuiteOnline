using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.SupplierReturns.Entities;

/// <summary>
/// A return of goods to a supplier — mirror of <see cref="Receiving.Entities.ReceivingOrder"/>
/// but flowing the other way. Lifecycle: Draft → Submitted → Completed (or Cancelled
/// at any point before Completed). Inventory is only deducted on Complete; the
/// <see cref="Services.SupplierReturnService"/> also issues a SupplierCreditNote then.
/// May optionally reference the original receiving order via <see cref="SourceReceivingOrderId"/>.
/// </summary>
public class SupplierReturn : TenantEntity
{
    /// <summary>Human-readable return number — "SR-202605-0001". Unique per tenant.</summary>
    public string ReturnNumber { get; private set; } = string.Empty;

    public Guid SupplierId { get; private set; }

    /// <summary>Optional link to the receiving order these goods originally came from.</summary>
    public Guid? SourceReceivingOrderId { get; private set; }

    public SupplierReturnStatus Status { get; private set; } = SupplierReturnStatus.Draft;
    public SupplierReturnReason Reason { get; private set; } = SupplierReturnReason.Damaged;

    public DateTime? SubmittedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    public string? Notes { get; private set; }
    public string Currency { get; private set; } = "PKR";

    /// <summary>Sum of (UnitCost × Quantity) across all lines — also the value of the credit note issued on Complete.</summary>
    public decimal TotalValue { get; private set; }

    private readonly List<SupplierReturnItem> _items = new();
    public IReadOnlyCollection<SupplierReturnItem> Items => _items;

    private SupplierReturn() { }

    public SupplierReturn(
        Guid tenantId,
        string returnNumber,
        Guid supplierId,
        SupplierReturnReason reason,
        Guid? sourceReceivingOrderId = null,
        string currency = "PKR")
    {
        if (string.IsNullOrWhiteSpace(returnNumber))
            throw new ArgumentException("ReturnNumber is required.", nameof(returnNumber));
        if (supplierId == Guid.Empty)
            throw new ArgumentException("SupplierId is required.", nameof(supplierId));

        Id                      = Guid.NewGuid();
        CreatedAt               = DateTime.UtcNow;
        TenantId                = tenantId;
        ReturnNumber            = returnNumber;
        SupplierId              = supplierId;
        Reason                  = reason;
        SourceReceivingOrderId  = sourceReceivingOrderId;
        Currency                = string.IsNullOrWhiteSpace(currency) ? "PKR" : currency.ToUpperInvariant();
    }

    public void SetNotes(string? notes) => Notes = notes;
    public void SetReason(SupplierReturnReason reason) => Reason = reason;

    public void AddItem(SupplierReturnItem item)
    {
        if (Status != SupplierReturnStatus.Draft)
            throw new BusinessRuleException("Only Draft returns can be modified.");
        if (item == null) throw new ArgumentNullException(nameof(item));

        _items.Add(item);
        RecalculateTotal();
    }

    public void RemoveItem(Guid itemId)
    {
        if (Status != SupplierReturnStatus.Draft)
            throw new BusinessRuleException("Only Draft returns can be modified.");

        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item == null) return;

        _items.Remove(item);
        RecalculateTotal();
    }

    public void ClearItems()
    {
        if (Status != SupplierReturnStatus.Draft)
            throw new BusinessRuleException("Only Draft returns can be modified.");
        _items.Clear();
        TotalValue = 0m;
    }

    /// <summary>Move from Draft to Submitted — locks the lines. No inventory impact yet.</summary>
    public void Submit()
    {
        if (Status != SupplierReturnStatus.Draft)
            throw new BusinessRuleException("Only Draft returns can be submitted.");
        if (_items.Count == 0)
            throw new BusinessRuleException("Cannot submit a return with no items.");

        Status      = SupplierReturnStatus.Submitted;
        SubmittedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Mark the return as Completed. Caller is responsible for deducting inventory and
    /// issuing the credit note — this method only flips the state.
    /// </summary>
    public void MarkCompleted()
    {
        if (Status != SupplierReturnStatus.Submitted && Status != SupplierReturnStatus.Draft)
            throw new BusinessRuleException("Only Draft or Submitted returns can be completed.");
        if (_items.Count == 0)
            throw new BusinessRuleException("Cannot complete a return with no items.");

        Status      = SupplierReturnStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        if (SubmittedAt == null) SubmittedAt = CompletedAt;
    }

    public void Cancel()
    {
        if (Status == SupplierReturnStatus.Completed)
            throw new BusinessRuleException("Completed returns cannot be cancelled.");
        Status      = SupplierReturnStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
    }

    private void RecalculateTotal() => TotalValue = _items.Sum(i => i.LineTotal);
}
