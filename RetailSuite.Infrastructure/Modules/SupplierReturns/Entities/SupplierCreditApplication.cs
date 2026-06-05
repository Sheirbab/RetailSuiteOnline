using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.SupplierReturns.Entities;

/// <summary>
/// One application of a <see cref="SupplierCreditNote"/> against a receiving order
/// (i.e. the credit was used to settle, or partially settle, a future PO from the
/// same supplier). Many applications can exist per credit note until <c>Remaining</c>
/// hits zero. Immutable once created — reversals would be a new negative application.
/// </summary>
public class SupplierCreditApplication : TenantEntity
{
    public Guid CreditNoteId      { get; private set; }
    public Guid ReceivingOrderId  { get; private set; }
    public Guid SupplierId        { get; private set; }
    public decimal Amount         { get; private set; }
    public DateTime AppliedAt     { get; private set; }
    public string? Notes          { get; private set; }

    private SupplierCreditApplication() { }

    public SupplierCreditApplication(
        Guid tenantId,
        Guid creditNoteId,
        Guid receivingOrderId,
        Guid supplierId,
        decimal amount,
        string? notes = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Applied amount must be positive.", nameof(amount));

        Id               = Guid.NewGuid();
        CreatedAt        = DateTime.UtcNow;
        TenantId         = tenantId;
        CreditNoteId     = creditNoteId;
        ReceivingOrderId = receivingOrderId;
        SupplierId       = supplierId;
        Amount           = amount;
        AppliedAt        = DateTime.UtcNow;
        Notes            = notes;
    }
}
