using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure.Email;
using RetailSuite.Infrastructure.Modules.Subscriptions.Entities;
using RetailSuite.Infrastructure.Modules.Tenant.Entities;

namespace RetailSuite.Infrastructure.Payments;

/// <summary>
/// Result of attempting to reconcile a webhook against our records.
/// </summary>
public record ReconciliationResult(
    bool Reconciled,
    Guid? SubscriptionPaymentId,
    Guid? InvoiceId,
    string? Reason);

/// <summary>
/// Reconciles inbound webhook outcomes against the SubscriptionPayments we created
/// when the customer initiated the charge. Shared by Stripe, EasyPaisa, JazzCash.
/// </summary>
public interface ISubscriptionPaymentReconciler
{
    /// <summary>
    /// Mark a pending SubscriptionPayment as Succeeded or Failed based on the webhook outcome.
    /// On success: applies amount to the invoice, advances the subscription period, emails the tenant,
    /// and restores tenant Status from PastDue/Suspended if applicable.
    /// </summary>
    Task<ReconciliationResult> ReconcileAsync(
        string providerTxnRef,
        bool succeeded,
        decimal amount,
        string? failureReason = null);
}

public class SubscriptionPaymentReconciler : ISubscriptionPaymentReconciler
{
    private readonly RetailDbContext _db;
    private readonly INotificationService _notify;
    private readonly ILogger<SubscriptionPaymentReconciler> _logger;

    public SubscriptionPaymentReconciler(
        RetailDbContext db,
        INotificationService notify,
        ILogger<SubscriptionPaymentReconciler> logger)
    {
        _db     = db;
        _notify = notify;
        _logger = logger;
    }

    public async Task<ReconciliationResult> ReconcileAsync(
        string providerTxnRef,
        bool succeeded,
        decimal amount,
        string? failureReason = null)
    {
        if (string.IsNullOrWhiteSpace(providerTxnRef))
            return new ReconciliationResult(false, null, null, "Missing transaction reference.");

        var payment = await _db.SubscriptionPayments
            .IgnoreQueryFilters()
            .Where(p => p.ProviderTxnRef == providerTxnRef
                     || (p.ProviderTxnRef == null && p.Status == SubscriptionPaymentStatus.Pending))
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (payment == null)
        {
            _logger.LogInformation(
                "Webhook reference {Ref} did not match any SubscriptionPayment.", providerTxnRef);
            return new ReconciliationResult(false, null, null, "No matching SubscriptionPayment.");
        }

        if (payment.Status == SubscriptionPaymentStatus.Succeeded)
        {
            // Already reconciled — idempotent no-op.
            return new ReconciliationResult(true, payment.Id, payment.InvoiceId, "Already succeeded.");
        }

        var invoice = await _db.SubscriptionInvoices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == payment.InvoiceId);

        if (invoice == null)
        {
            _logger.LogWarning(
                "Webhook reconcile: SubscriptionPayment {PaymentId} has no matching invoice.", payment.Id);
            return new ReconciliationResult(false, payment.Id, null, "Invoice missing.");
        }

        if (!succeeded)
        {
            payment.MarkFailed(failureReason ?? "Provider reported failure");
            await _db.SaveChangesAsync();
            _logger.LogWarning(
                "Reconciled failure: Payment={PaymentId}, Invoice={Number}, Reason={Reason}",
                payment.Id, invoice.InvoiceNumber, failureReason);
            return new ReconciliationResult(true, payment.Id, invoice.Id, failureReason);
        }

        // Success path.
        payment.MarkSucceeded(providerTxnRef);
        invoice.ApplyPayment(payment.Amount);

        // Advance the subscription if this was a renewal invoice.
        if (invoice.Reason.StartsWith("Renewal", StringComparison.OrdinalIgnoreCase))
        {
            var sub = await _db.TenantSubscriptions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == invoice.SubscriptionId);
            sub?.RenewToNextPeriod();
        }

        // Restore tenant status if non-payment had pushed them out.
        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == payment.TenantId);
        if (tenant != null && (tenant.Status == TenantStatus.PastDue
                            || tenant.Status == TenantStatus.Suspended))
        {
            tenant.SetStatus(TenantStatus.Active);
        }

        await _db.SaveChangesAsync();

        // Best-effort email — don't fail the webhook on email errors.
        try
        {
            var (toAddress, recipientName, tenantName) = await ResolveRecipientAsync(payment.TenantId);
            if (!string.IsNullOrWhiteSpace(toAddress))
            {
                await _notify.SendInvoicePaidAsync(
                    toAddress, recipientName, tenantName,
                    invoice.InvoiceNumber, payment.Amount, invoice.Currency, payment.PaymentMethod,
                    tenantId: payment.TenantId, invoiceId: invoice.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send InvoicePaid email for invoice {Number}.", invoice.InvoiceNumber);
        }

        _logger.LogInformation(
            "Reconciled success: Payment={PaymentId}, Invoice={Number}, Amount={Amount}",
            payment.Id, invoice.InvoiceNumber, payment.Amount);

        return new ReconciliationResult(true, payment.Id, invoice.Id, null);
    }

    private async Task<(string toAddress, string recipientName, string tenantName)>
        ResolveRecipientAsync(Guid tenantId)
    {
        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant == null) return (string.Empty, string.Empty, string.Empty);

        var to = tenant.BillingEmail;
        if (string.IsNullOrWhiteSpace(to))
        {
            to = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => u.TenantId == tenantId && !u.IsDeleted)
                .OrderBy(u => u.CreatedAt)
                .Select(u => u.Email)
                .FirstOrDefaultAsync();
        }

        return (to ?? string.Empty, to ?? tenant.Name, tenant.Name);
    }
}
