using System.Text.Json;
using RetailSuite.Shared;

namespace RetailSuite.Modules.Orders.Entities;

/// <summary>
/// A POS cart parked mid-sale so the cashier can serve the next customer.
/// Snapshot is stored as JSON to keep the entity simple — when resumed, the snapshot
/// is rehydrated into a draft Order. Stock is NOT reserved by a held sale, so the
/// last-second resume may discover an out-of-stock item.
/// </summary>
public class HeldSale : TenantEntity
{
    /// <summary>Cashier who parked the sale. Resume should ideally be done by the same person.</summary>
    public Guid CashierUserId { get; private set; }

    /// <summary>Optional label cashier gave the parked cart — "Customer with the red bag".</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>If the customer was attached to the cart before parking.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>Customer's phone if it was looked up at hold time — useful for resume search.</summary>
    public string? CustomerPhone { get; private set; }

    /// <summary>JSON snapshot of cart lines: [{ variantId, sku, qty, unitPrice, lineDiscount }, ...].</summary>
    public string CartJson { get; private set; } = "[]";

    /// <summary>Order-level discount captured at hold time.</summary>
    public decimal OrderDiscountAmount { get; private set; }

    public string? Notes { get; private set; }

    private HeldSale() { }

    public HeldSale(
        Guid tenantId,
        Guid cashierUserId,
        string label,
        Guid? customerId,
        string? customerPhone,
        IEnumerable<HeldCartLine> lines,
        decimal orderDiscountAmount,
        string? notes)
    {
        Id                  = Guid.NewGuid();
        CreatedAt           = DateTime.UtcNow;
        TenantId            = tenantId;
        CashierUserId       = cashierUserId;
        Label               = string.IsNullOrWhiteSpace(label) ? "Held sale" : label.Trim();
        CustomerId          = customerId;
        CustomerPhone       = customerPhone;
        CartJson            = JsonSerializer.Serialize(lines);
        OrderDiscountAmount = orderDiscountAmount;
        Notes               = notes;
    }

    public List<HeldCartLine> GetLines()
    {
        if (string.IsNullOrWhiteSpace(CartJson)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<HeldCartLine>>(CartJson) ?? new();
        }
        catch { return new(); }
    }
}

/// <summary>Snapshot of a single cart line captured when parked.</summary>
public record HeldCartLine(
    Guid VariantId,
    string Sku,
    int Quantity,
    decimal UnitPrice,
    decimal LineDiscountAmount,
    decimal TaxRate);
