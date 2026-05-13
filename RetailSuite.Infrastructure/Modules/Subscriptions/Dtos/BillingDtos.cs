using RetailSuite.Infrastructure.Modules.Subscriptions.Entities;

namespace RetailSuite.Infrastructure.Modules.Subscriptions.Dtos;

public record InvoiceResponse(
    Guid Id,
    string InvoiceNumber,
    string PlanCode,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    decimal Subtotal,
    decimal TaxAmount,
    decimal Total,
    decimal AmountPaid,
    decimal AmountDue,
    string Currency,
    string Status,
    DateTime DueDate,
    DateTime? PaidAt,
    string Reason,
    DateTime CreatedAt);

public record PaymentResponse(
    Guid Id,
    Guid InvoiceId,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    string Provider,
    string? ProviderTxnRef,
    string Status,
    string? FailureReason,
    DateTime? PaidAt,
    DateTime CreatedAt);

public class PayInvoiceRequest
{
    /// <summary>"Stripe" | "EasyPaisa" | "JazzCash" | "Cash" | "BankTransfer" | "Fake".</summary>
    public string PaymentMethod { get; set; } = string.Empty;
}

public class MarkPaidRequest
{
    public string PaymentMethod { get; set; } = "BankTransfer";
    public string? ExternalReference { get; set; }
}

public static class BillingMappers
{
    public static InvoiceResponse ToResponse(this SubscriptionInvoice i) => new(
        i.Id, i.InvoiceNumber, i.PlanCode,
        i.PeriodStart, i.PeriodEnd,
        i.Subtotal, i.TaxAmount, i.Total,
        i.AmountPaid, i.AmountDue, i.Currency,
        i.Status.ToString(),
        i.DueDate, i.PaidAt, i.Reason, i.CreatedAt);

    public static PaymentResponse ToResponse(this SubscriptionPayment p) => new(
        p.Id, p.InvoiceId,
        p.Amount, p.Currency,
        p.PaymentMethod, p.Provider, p.ProviderTxnRef,
        p.Status.ToString(), p.FailureReason, p.PaidAt, p.CreatedAt);
}
