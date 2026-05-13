using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Payments;

/// <summary>
/// Persistent record of every inbound webhook delivery.
/// Two jobs:
///   1. Idempotency — providers retry on 5xx, so dedupe on (Provider, ExternalEventId).
///   2. Audit / replay — full raw payload kept for support investigations.
/// </summary>
/// <remarks>
/// Not tenant-scoped because webhooks arrive before any auth — tenant is resolved
/// during processing from the matching SubscriptionPayment.
/// </remarks>
public class WebhookEvent : BaseEntity
{
    /// <summary>"Stripe" | "EasyPaisa" | "JazzCash".</summary>
    public string Provider { get; private set; } = string.Empty;

    /// <summary>Provider-issued event identifier. Required for idempotency.</summary>
    public string ExternalEventId { get; private set; } = string.Empty;

    /// <summary>Provider event type — e.g. "charge.succeeded", "0000" code for EP.</summary>
    public string EventType { get; private set; } = string.Empty;

    /// <summary>Raw request body as received (for replay / audit).</summary>
    public string RawPayload { get; private set; } = string.Empty;

    /// <summary>True once the handler has finished processing this delivery.</summary>
    public bool Processed { get; private set; }

    /// <summary>UTC time the handler finished. Null while pending or failed.</summary>
    public DateTime? ProcessedAt { get; private set; }

    /// <summary>Last error encountered during processing (null on success).</summary>
    public string? ProcessingError { get; private set; }

    /// <summary>For traceability — the SubscriptionPayment we matched on, if any.</summary>
    public Guid? MatchedSubscriptionPaymentId { get; private set; }

    /// <summary>For traceability — the Order Payment we matched on, if any.</summary>
    public Guid? MatchedOrderPaymentId { get; private set; }

    private WebhookEvent() { }

    public WebhookEvent(string provider, string externalEventId, string eventType, string rawPayload)
    {
        Id              = Guid.NewGuid();
        CreatedAt       = DateTime.UtcNow;
        Provider        = provider;
        ExternalEventId = externalEventId;
        EventType       = eventType ?? string.Empty;
        RawPayload      = rawPayload ?? string.Empty;
        Processed       = false;
    }

    public void MarkProcessed(Guid? subscriptionPaymentId = null, Guid? orderPaymentId = null)
    {
        Processed                   = true;
        ProcessedAt                 = DateTime.UtcNow;
        ProcessingError             = null;
        MatchedSubscriptionPaymentId = subscriptionPaymentId;
        MatchedOrderPaymentId        = orderPaymentId;
    }

    public void MarkFailed(string error)
    {
        Processed       = false;
        ProcessedAt     = DateTime.UtcNow;
        ProcessingError = error?.Length > 1000 ? error.Substring(0, 1000) : error;
    }
}
