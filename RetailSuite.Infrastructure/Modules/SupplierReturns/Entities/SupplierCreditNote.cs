using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.SupplierReturns.Entities;

/// <summary>
/// Credit balance owed by the supplier following a completed return. Issued
/// automatically by <see cref="Services.SupplierReturnService"/> when a return
/// transitions to Completed. The remaining balance can be applied against future
/// receiving orders / supplier invoices.
/// </summary>
public class SupplierCreditNote : TenantEntity
{
    /// <summary>"SCN-202605-0001". Unique per tenant.</summary>
    public string CreditNoteNumber { get; private set; } = string.Empty;

    public Guid SupplierId { get; private set; }

    /// <summary>The return that produced this credit. 1:1 — at most one credit per return.</summary>
    public Guid SupplierReturnId { get; private set; }

    public DateTime IssuedAt { get; private set; }

    /// <summary>The face value of the credit when issued. Immutable.</summary>
    public decimal Amount { get; private set; }

    /// <summary>How much of the credit has been applied against future POs / invoices.</summary>
    public decimal AppliedAmount { get; private set; }

    /// <summary>Remaining balance the supplier still owes the shop.</summary>
    public decimal Remaining => Math.Max(0m, Amount - AppliedAmount);

    public string Currency { get; private set; } = "PKR";

    public string? Notes { get; private set; }

    private SupplierCreditNote() { }

    public SupplierCreditNote(
        Guid tenantId,
        string creditNoteNumber,
        Guid supplierId,
        Guid supplierReturnId,
        decimal amount,
        string currency = "PKR",
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(creditNoteNumber))
            throw new ArgumentException("CreditNoteNumber is required.", nameof(creditNoteNumber));
        if (amount <= 0)
            throw new ArgumentException("Credit-note amount must be positive.", nameof(amount));

        Id               = Guid.NewGuid();
        CreatedAt        = DateTime.UtcNow;
        TenantId         = tenantId;
        CreditNoteNumber = creditNoteNumber;
        SupplierId       = supplierId;
        SupplierReturnId = supplierReturnId;
        Amount           = amount;
        IssuedAt         = DateTime.UtcNow;
        Currency         = string.IsNullOrWhiteSpace(currency) ? "PKR" : currency.ToUpperInvariant();
        Notes            = notes;
    }

    /// <summary>Apply <paramref name="amount"/> of this credit against a future PO/invoice. Throws if it would exceed the remaining balance.</summary>
    public void Apply(decimal amount)
    {
        if (amount <= 0)
            throw new BusinessRuleException("Applied amount must be positive.");
        if (amount > Remaining)
            throw new BusinessRuleException($"Cannot apply Rs {amount:N2} — only Rs {Remaining:N2} remaining on credit note.");

        AppliedAmount += amount;
    }

    public void SetNotes(string? notes) => Notes = notes;
}
