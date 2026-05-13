namespace RetailSuite.Infrastructure.Modules.Subscriptions.Entities;

/// <summary>Lifecycle of a subscription invoice.</summary>
public enum InvoiceStatus
{
    /// <summary>Issued but not yet paid; before due date.</summary>
    Open      = 0,

    /// <summary>Fully paid.</summary>
    Paid      = 1,

    /// <summary>Past due date and still unpaid.</summary>
    Overdue   = 2,

    /// <summary>Cancelled / written off — never collected.</summary>
    Void      = 3,

    /// <summary>Refunded after payment.</summary>
    Refunded  = 4
}

/// <summary>Lifecycle of a subscription payment attempt.</summary>
public enum SubscriptionPaymentStatus
{
    /// <summary>Created but not yet confirmed (waiting for webhook / manual approval).</summary>
    Pending   = 0,

    /// <summary>Provider confirmed the payment.</summary>
    Succeeded = 1,

    /// <summary>Provider returned a failure.</summary>
    Failed    = 2,

    /// <summary>Refunded to the original payment method.</summary>
    Refunded  = 3
}
