using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Subscriptions.Entities;

/// <summary>
/// A single attempt to pay a subscription invoice — there may be multiple per invoice
/// (e.g. failed JazzCash retry followed by a successful Stripe charge).
/// </summary>
public class SubscriptionPayment : TenantEntity
{
    public Guid InvoiceId { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "PKR";

    /// <summary>Display label: "Stripe", "EasyPaisa", "JazzCash", "BankTransfer", "Cash".</summary>
    public string PaymentMethod { get; private set; } = string.Empty;

    /// <summary>Gateway / provider identifier. Same as PaymentMethod for non-gateway methods.</summary>
    public string Provider { get; private set; } = string.Empty;

    /// <summary>Gateway-issued transaction id (null until succeeded).</summary>
    public string? ProviderTxnRef { get; private set; }

    public SubscriptionPaymentStatus Status { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTime? PaidAt { get; private set; }

    private SubscriptionPayment() { }

    public SubscriptionPayment(
        Guid tenantId,
        Guid invoiceId,
        decimal amount,
        string currency,
        string paymentMethod,
        string provider)
    {
        Id            = Guid.NewGuid();
        CreatedAt     = DateTime.UtcNow;
        TenantId      = tenantId;
        InvoiceId     = invoiceId;
        Amount        = amount;
        Currency      = string.IsNullOrWhiteSpace(currency) ? "PKR" : currency.ToUpperInvariant();
        PaymentMethod = paymentMethod;
        Provider      = provider;
        Status        = SubscriptionPaymentStatus.Pending;
    }

    public void MarkSucceeded(string? providerTxnRef)
    {
        Status         = SubscriptionPaymentStatus.Succeeded;
        ProviderTxnRef = providerTxnRef;
        PaidAt         = DateTime.UtcNow;
        FailureReason  = null;
    }

    public void MarkFailed(string reason)
    {
        Status        = SubscriptionPaymentStatus.Failed;
        FailureReason = reason?.Length > 500 ? reason.Substring(0, 500) : reason;
    }

    public void MarkRefunded() => Status = SubscriptionPaymentStatus.Refunded;
}
