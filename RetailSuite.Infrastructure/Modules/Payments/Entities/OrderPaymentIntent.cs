using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Payments.Entities;

/// <summary>
/// A payment attempt against an order. One Order can have many intents — typically
/// one active Pending intent at a time. Created when the customer selects EasyPaisa /
/// JazzCash at checkout; closed by the inbound gateway webhook (Paid/Failed) or by
/// expiry (Expired). Independent of the actual Payment ledger entry — that's recorded
/// by the order service once the intent moves to Paid.
/// </summary>
public class OrderPaymentIntent : TenantEntity
{
    public Guid OrderId { get; private set; }

    /// <summary>"EasyPaisa", "JazzCash", "Stripe". Stored as a string so the column survives provider changes.</summary>
    public string Provider { get; private set; } = string.Empty;

    /// <summary>Gateway-assigned transaction id once the gateway acknowledges the charge initiation.</summary>
    public string? ProviderTxnId { get; private set; }

    public decimal AmountDue { get; private set; }
    public string Currency { get; private set; } = "PKR";

    public PaymentIntentStatus Status { get; private set; } = PaymentIntentStatus.Pending;

    /// <summary>
    /// Opaque QR data string the customer scans. Encoded format depends on the provider —
    /// for EP/JC sandbox we use a simple "provider:txnId:amount" payload; replace with the
    /// gateway-issued QR string once on live credentials.
    /// </summary>
    public string? QrPayload { get; private set; }

    /// <summary>When the QR / payment link expires. After this, the intent auto-expires on first read.</summary>
    public DateTime ExpiresAt { get; private set; }

    public DateTime? PaidAt { get; private set; }
    public DateTime? FailedAt { get; private set; }
    public string? FailureReason { get; private set; }

    private OrderPaymentIntent() { }

    public OrderPaymentIntent(
        Guid tenantId,
        Guid orderId,
        string provider,
        decimal amountDue,
        string currency = "PKR",
        int validForMinutes = 30)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider is required.", nameof(provider));
        if (amountDue <= 0)
            throw new ArgumentException("AmountDue must be positive.", nameof(amountDue));

        Id        = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        TenantId  = tenantId;
        OrderId   = orderId;
        Provider  = provider.Trim();
        AmountDue = amountDue;
        Currency  = string.IsNullOrWhiteSpace(currency) ? "PKR" : currency.ToUpperInvariant();
        ExpiresAt = CreatedAt.AddMinutes(Math.Max(5, validForMinutes));
    }

    public void SetGatewayTransaction(string providerTxnId, string? qrPayload)
    {
        if (string.IsNullOrWhiteSpace(providerTxnId))
            throw new ArgumentException("ProviderTxnId is required.", nameof(providerTxnId));
        ProviderTxnId = providerTxnId.Trim();
        QrPayload     = qrPayload;
    }

    public void MarkPaid(string? gatewayTxnId = null)
    {
        if (Status == PaymentIntentStatus.Paid) return;
        if (Status == PaymentIntentStatus.Failed || Status == PaymentIntentStatus.Expired)
            throw new BusinessRuleException($"Cannot mark a {Status} intent as Paid.");

        if (!string.IsNullOrWhiteSpace(gatewayTxnId))
            ProviderTxnId = gatewayTxnId.Trim();

        Status = PaymentIntentStatus.Paid;
        PaidAt = DateTime.UtcNow;
    }

    public void MarkFailed(string? reason)
    {
        if (Status == PaymentIntentStatus.Paid)
            throw new BusinessRuleException("Cannot mark a Paid intent as Failed.");
        Status        = PaymentIntentStatus.Failed;
        FailedAt      = DateTime.UtcNow;
        FailureReason = reason;
    }

    public void MarkExpired()
    {
        if (Status != PaymentIntentStatus.Pending) return;
        Status   = PaymentIntentStatus.Expired;
        FailedAt = DateTime.UtcNow;
    }

    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
}

public enum PaymentIntentStatus
{
    Pending = 1,
    Paid    = 2,
    Failed  = 3,
    Expired = 4
}
