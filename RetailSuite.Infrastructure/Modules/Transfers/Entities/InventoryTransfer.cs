using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Transfers.Entities;

/// <summary>
/// Moves stock from one branch to another. Lifecycle: Draft → InTransit → Received,
/// or Cancelled from Draft / InTransit. Stock deducts at source on Submit; appears
/// at destination on Receive. If Cancelled while InTransit, source stock is restored.
///
/// The aggregate only manages state transitions; the inventory side-effects are owned
/// by <see cref="Services.IInventoryTransferService"/>.
/// </summary>
public class InventoryTransfer : TenantEntity
{
    /// <summary>Human-readable number — "TRF-202606-0001". Unique per tenant.</summary>
    public string TransferNumber { get; private set; } = string.Empty;

    public Guid SourceLocationId { get; private set; }
    public Guid DestinationLocationId { get; private set; }

    public TransferStatus Status { get; private set; } = TransferStatus.Draft;

    public DateTime? SubmittedAt { get; private set; }
    public DateTime? ReceivedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    public string? Notes { get; private set; }
    public string Currency { get; private set; } = "PKR";

    public decimal TotalValue { get; private set; }

    private readonly List<InventoryTransferItem> _items = new();
    public IReadOnlyCollection<InventoryTransferItem> Items => _items;

    private InventoryTransfer() { }

    public InventoryTransfer(
        Guid tenantId,
        string transferNumber,
        Guid sourceLocationId,
        Guid destinationLocationId,
        string currency = "PKR")
    {
        if (string.IsNullOrWhiteSpace(transferNumber))
            throw new ArgumentException("TransferNumber is required.", nameof(transferNumber));
        if (sourceLocationId == Guid.Empty || destinationLocationId == Guid.Empty)
            throw new ArgumentException("Source and destination locations are required.");
        if (sourceLocationId == destinationLocationId)
            throw new BusinessRuleException("Source and destination cannot be the same location.");

        Id                    = Guid.NewGuid();
        CreatedAt             = DateTime.UtcNow;
        TenantId              = tenantId;
        TransferNumber        = transferNumber;
        SourceLocationId      = sourceLocationId;
        DestinationLocationId = destinationLocationId;
        Currency              = string.IsNullOrWhiteSpace(currency) ? "PKR" : currency.ToUpperInvariant();
    }

    public void SetNotes(string? notes) => Notes = notes;

    public void AddItem(InventoryTransferItem item)
    {
        if (Status != TransferStatus.Draft)
            throw new BusinessRuleException("Only Draft transfers can be modified.");
        if (item == null) throw new ArgumentNullException(nameof(item));

        _items.Add(item);
        Recalculate();
    }

    public void RemoveItem(Guid itemId)
    {
        if (Status != TransferStatus.Draft)
            throw new BusinessRuleException("Only Draft transfers can be modified.");

        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item == null) return;
        _items.Remove(item);
        Recalculate();
    }

    /// <summary>Move Draft → InTransit. Service is responsible for actually deducting source stock.</summary>
    public void MarkInTransit()
    {
        if (Status != TransferStatus.Draft)
            throw new BusinessRuleException("Only Draft transfers can be submitted.");
        if (_items.Count == 0)
            throw new BusinessRuleException("Cannot submit a transfer with no items.");

        Status      = TransferStatus.InTransit;
        SubmittedAt = DateTime.UtcNow;
    }

    /// <summary>Move InTransit → Received. Service is responsible for adding destination stock.</summary>
    public void MarkReceived()
    {
        if (Status != TransferStatus.InTransit)
            throw new BusinessRuleException("Only InTransit transfers can be received.");

        Status     = TransferStatus.Received;
        ReceivedAt = DateTime.UtcNow;
    }

    /// <summary>Cancel from Draft or InTransit. Service restores source stock if InTransit.</summary>
    public void MarkCancelled()
    {
        if (Status == TransferStatus.Received)
            throw new BusinessRuleException("Received transfers cannot be cancelled.");
        if (Status == TransferStatus.Cancelled)
            return;

        Status      = TransferStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
    }

    private void Recalculate() => TotalValue = _items.Sum(i => i.LineTotal);
}
