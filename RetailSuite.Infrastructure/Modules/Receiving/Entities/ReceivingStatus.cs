namespace RetailSuite.Infrastructure.Modules.Receiving.Entities;

/// <summary>Lifecycle of a purchase-order / receiving-order.</summary>
public enum ReceivingStatus
{
    /// <summary>Being edited; not yet committed to the supplier or to inventory.</summary>
    Draft               = 0,

    /// <summary>Submitted — awaiting goods. Editable lines locked.</summary>
    Open                = 1,

    /// <summary>Some lines received but not all expected quantities.</summary>
    PartiallyReceived   = 2,

    /// <summary>All lines received; order closed.</summary>
    Closed              = 3,

    /// <summary>Voided before completion. No inventory impact.</summary>
    Cancelled           = 4
}

/// <summary>Per-line status, derived from quantities.</summary>
public enum ReceivingLineStatus
{
    Pending              = 0,
    PartiallyReceived    = 1,
    Received             = 2
}
