namespace RetailSuite.Infrastructure.Modules.Transfers.Entities;

/// <summary>
/// Lifecycle of an inter-location stock transfer.
///   Draft     — being built at source; stock untouched.
///   InTransit — source has handed goods to courier. Stock deducted from source; not yet at destination.
///   Received  — destination has confirmed receipt. Stock added at destination.
///   Cancelled — abandoned. From Draft: no stock effect. From InTransit: source stock restored.
/// </summary>
public enum TransferStatus
{
    Draft     = 1,
    InTransit = 2,
    Received  = 3,
    Cancelled = 4
}
