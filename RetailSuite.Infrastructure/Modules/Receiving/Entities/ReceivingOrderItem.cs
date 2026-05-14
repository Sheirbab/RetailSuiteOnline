using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Receiving.Entities;

/// <summary>
/// A single line on a <see cref="ReceivingOrder"/> — one product variant + expected quantity
/// + expected unit cost. Receipts accumulate into <see cref="ReceivedQuantity"/>.
/// </summary>
public class ReceivingOrderItem : TenantEntity
{
    public Guid ReceivingOrderId { get; private set; }
    public Guid ProductVariantId { get; private set; }

    /// <summary>SKU snapshot at the time the line was created — useful for historical reporting.</summary>
    public string Sku { get; private set; } = string.Empty;

    public int ExpectedQuantity { get; private set; }
    public int ReceivedQuantity { get; private set; }

    public decimal UnitCost { get; private set; }

    public ReceivingLineStatus Status { get; private set; } = ReceivingLineStatus.Pending;

    public string? Notes { get; private set; }

    private ReceivingOrderItem() { }

    public ReceivingOrderItem(
        Guid tenantId,
        Guid receivingOrderId,
        Guid productVariantId,
        string sku,
        int expectedQuantity,
        decimal unitCost,
        string? notes = null)
    {
        if (expectedQuantity <= 0)
            throw new ArgumentException("ExpectedQuantity must be positive.", nameof(expectedQuantity));
        if (unitCost < 0)
            throw new ArgumentException("UnitCost cannot be negative.", nameof(unitCost));

        Id               = Guid.NewGuid();
        CreatedAt        = DateTime.UtcNow;
        TenantId         = tenantId;
        ReceivingOrderId = receivingOrderId;
        ProductVariantId = productVariantId;
        Sku              = sku ?? string.Empty;
        ExpectedQuantity = expectedQuantity;
        UnitCost         = unitCost;
        Notes            = notes;
    }

    /// <summary>How many units remain to be received against this line.</summary>
    public int OutstandingQuantity => Math.Max(0, ExpectedQuantity - ReceivedQuantity);

    /// <summary>
    /// Apply an additional receipt of <paramref name="qty"/> units. Caller must have already
    /// validated that the parent order is in a state that allows receipts.
    /// </summary>
    public void AddReceipt(int qty)
    {
        if (qty <= 0)
            throw new BusinessRuleException("Receipt quantity must be positive.");
        if (ReceivedQuantity + qty > ExpectedQuantity)
            throw new BusinessRuleException(
                $"Receipt of {qty} would exceed expected quantity {ExpectedQuantity}. " +
                $"Outstanding: {OutstandingQuantity}.");

        ReceivedQuantity += qty;

        Status = ReceivedQuantity >= ExpectedQuantity
            ? ReceivingLineStatus.Received
            : ReceivingLineStatus.PartiallyReceived;
    }

    public void SetNotes(string? notes) => Notes = notes;
}
