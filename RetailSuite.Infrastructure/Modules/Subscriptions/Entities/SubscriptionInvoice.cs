using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Subscriptions.Entities;

/// <summary>
/// A bill issued to a tenant for a subscription period (renewal, proration, etc).
/// Total = Subtotal + TaxAmount. Per current org decision (Sub-phase 3b/3c), prices are
/// stated as tax-inclusive so TaxAmount is always 0; the column is kept for future use.
/// </summary>
public class SubscriptionInvoice : TenantEntity
{
    public Guid SubscriptionId { get; private set; }

    /// <summary>Human-readable invoice number — unique per tenant.</summary>
    public string InvoiceNumber { get; private set; } = string.Empty;

    /// <summary>Plan code at the time the invoice was issued.</summary>
    public string PlanCode { get; private set; } = string.Empty;

    public DateTime PeriodStart { get; private set; }
    public DateTime PeriodEnd { get; private set; }

    /// <summary>Subtotal before tax.</summary>
    public decimal Subtotal { get; private set; }

    /// <summary>Tax amount — 0 while we run tax-inclusive pricing.</summary>
    public decimal TaxAmount { get; private set; }

    /// <summary>Subtotal + TaxAmount.</summary>
    public decimal Total { get; private set; }

    /// <summary>ISO 4217 currency. PKR by default.</summary>
    public string Currency { get; private set; } = "PKR";

    public InvoiceStatus Status { get; private set; }

    public DateTime DueDate { get; private set; }
    public DateTime? PaidAt { get; private set; }

    /// <summary>Free-text reason this invoice was issued (e.g. "Renewal", "Upgrade proration").</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>Sum of successful payments — denormalised for quick "remaining" calc.</summary>
    public decimal AmountPaid { get; private set; }

    public decimal AmountDue => Math.Max(0, Total - AmountPaid);

    private SubscriptionInvoice() { }

    public SubscriptionInvoice(
        Guid tenantId,
        Guid subscriptionId,
        string invoiceNumber,
        string planCode,
        DateTime periodStart,
        DateTime periodEnd,
        decimal subtotal,
        string currency,
        DateTime dueDate,
        string reason)
    {
        Id              = Guid.NewGuid();
        CreatedAt       = DateTime.UtcNow;
        TenantId        = tenantId;
        SubscriptionId  = subscriptionId;
        InvoiceNumber   = invoiceNumber;
        PlanCode        = planCode;
        PeriodStart     = periodStart;
        PeriodEnd       = periodEnd;
        Subtotal        = subtotal;
        TaxAmount       = 0m;                // tax-inclusive pricing
        Total           = subtotal;
        Currency        = string.IsNullOrWhiteSpace(currency) ? "PKR" : currency.ToUpperInvariant();
        Status          = InvoiceStatus.Open;
        DueDate         = dueDate;
        Reason          = reason ?? string.Empty;
    }

    /// <summary>Add a successful payment toward this invoice. Closes the invoice if fully paid.</summary>
    public void ApplyPayment(decimal amount)
    {
        if (amount <= 0) return;
        AmountPaid += amount;
        if (AmountPaid >= Total)
        {
            Status = InvoiceStatus.Paid;
            PaidAt = DateTime.UtcNow;
        }
    }

    public void MarkOverdue()
    {
        if (Status == InvoiceStatus.Open) Status = InvoiceStatus.Overdue;
    }

    public void Void()
    {
        if (Status == InvoiceStatus.Paid || Status == InvoiceStatus.Refunded) return;
        Status = InvoiceStatus.Void;
    }

    public void MarkRefunded() => Status = InvoiceStatus.Refunded;
}
