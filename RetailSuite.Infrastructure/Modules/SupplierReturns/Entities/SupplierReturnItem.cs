using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.SupplierReturns.Entities;

/// <summary>
/// One line on a supplier return — one product variant + quantity + cost.
/// Quantity is positive (the service applies the negative sign when adjusting inventory).
/// </summary>
public class SupplierReturnItem : TenantEntity
{
    public Guid SupplierReturnId { get; private set; }
    public Guid ProductVariantId { get; private set; }

    /// <summary>SKU snapshot at the time the line was added — survives variant renames.</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>How many units are being returned. Always positive.</summary>
    public int Quantity { get; private set; }

    /// <summary>Unit cost at which the credit will be issued. Typically the original PO cost or the current average cost.</summary>
    public decimal UnitCost { get; private set; }

    /// <summary>Optional per-line override of the header reason — e.g. "torn label" on one of two lines on a Damaged return.</summary>
    public string? Notes { get; private set; }

    public decimal LineTotal => UnitCost * Quantity;

    private SupplierReturnItem() { }

    public SupplierReturnItem(
        Guid tenantId,
        Guid supplierReturnId,
        Guid productVariantId,
        string sku,
        int quantity,
        decimal unitCost,
        string? notes = null)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive.", nameof(quantity));
        if (unitCost < 0)
            throw new ArgumentException("UnitCost cannot be negative.", nameof(unitCost));

        Id               = Guid.NewGuid();
        CreatedAt        = DateTime.UtcNow;
        TenantId         = tenantId;
        SupplierReturnId = supplierReturnId;
        ProductVariantId = productVariantId;
        Sku              = sku ?? string.Empty;
        Quantity         = quantity;
        UnitCost         = unitCost;
        Notes            = notes;
    }

    public void SetNotes(string? notes) => Notes = notes;
}
