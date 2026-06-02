namespace RetailSuite.Infrastructure.Modules.SupplierReturns.Entities;

/// <summary>
/// Lifecycle of a supplier return.
///   Draft     — being built, lines editable, no inventory impact
///   Submitted — finalised; awaiting physical hand-off / pickup. No inventory impact yet.
///   Completed — goods have left the shop; inventory deducted and a supplier credit note issued.
///   Cancelled — abandoned before completion. No inventory impact.
/// </summary>
public enum SupplierReturnStatus
{
    Draft     = 1,
    Submitted = 2,
    Completed = 3,
    Cancelled = 4
}
