namespace RetailSuite.Infrastructure.Modules.SupplierReturns.Entities;

/// <summary>
/// Why we're returning goods to the supplier. Captured at the header level
/// (with per-line override via Notes). Used in reporting and for the
/// credit-note narrative.
/// </summary>
public enum SupplierReturnReason
{
    /// <summary>Items arrived broken or developed faults.</summary>
    Damaged       = 1,

    /// <summary>Supplier shipped the wrong SKU.</summary>
    WrongItem     = 2,

    /// <summary>Supplier shipped more than ordered.</summary>
    OverShipment  = 3,

    /// <summary>Catch-all for other reasons (recall, near-expiry, agreed swap, etc.).</summary>
    Other         = 9
}
