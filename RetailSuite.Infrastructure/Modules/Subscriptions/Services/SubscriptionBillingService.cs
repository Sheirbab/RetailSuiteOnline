using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Subscriptions.Entities;
using RetailSuite.Infrastructure.Payments;

namespace RetailSuite.Infrastructure.Modules.Subscriptions.Services;

/// <summary>
/// Orchestrates subscription invoicing + payment.
/// Invoice generation lives here (called by renewal job + plan changes).
/// Payment routing goes through <see cref="IPaymentGatewayFactory"/> so each invoice
/// can be paid with a different method (Stripe / EasyPaisa / JazzCash / Cash / BankTransfer).
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Generate an invoice for the subscription's current renewal period.</summary>
    Task<SubscriptionInvoice> GenerateRenewalInvoiceAsync(Guid tenantId, Guid subscriptionId);

    /// <summary>Generate a one-off invoice for a proration charge resulting from an upgrade.</summary>
    Task<SubscriptionInvoice> GenerateProrationInvoiceAsync(
        Guid tenantId,
        Guid subscriptionId,
        decimal amount,
        string planCode,
        string currency,
        string reason);

    /// <summary>
    /// Attempt to pay an invoice via a named payment provider. Routes through the gateway factory.
    /// Bank transfer and Cash methods return Pending (require manual confirmation by SuperAdmin).
    /// </summary>
    Task<SubscriptionPayment> PayInvoiceAsync(Guid tenantId, Guid invoiceId, string paymentMethod);

    /// <summary>Manually mark an invoice paid — used for bank-transfer reconciliation by SuperAdmin.</summary>
    Task<SubscriptionPayment> RecordManualPaymentAsync(
        Guid tenantId,
        Guid invoiceId,
        string paymentMethod,
        string? externalRef);
}

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private readonly RetailDbContext _db;
    private readonly IInvoiceNumberGenerator _invoiceNumbers;
    private readonly IPaymentGatewayFactory _gatewayFactory;
    private readonly ISubscriptionService _subs;
    private readonly ILogger<SubscriptionBillingService> _logger;

    /// <summary>Methods that do not flow through a gateway (manual reconciliation required).</summary>
    private static readonly HashSet<string> ManualMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "BankTransfer",
        "Cash"
    };

    public SubscriptionBillingService(
        RetailDbContext db,
        IInvoiceNumberGenerator invoiceNumbers,
        IPaymentGatewayFactory gatewayFactory,
        ISubscriptionService subs,
        ILogger<SubscriptionBillingService> logger)
    {
        _db             = db;
        _invoiceNumbers = invoiceNumbers;
        _gatewayFactory = gatewayFactory;
        _subs           = subs;
        _logger         = logger;
    }

    // ---------------------------------------------------------------
    // Invoice generation
    // ---------------------------------------------------------------

    public async Task<SubscriptionInvoice> GenerateRenewalInvoiceAsync(Guid tenantId, Guid subscriptionId)
    {
        var sub = await _db.TenantSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.TenantId == tenantId)
            ?? throw new NotFoundException("TenantSubscription", subscriptionId);

        var plan = await _db.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Id == sub.PlanId)
            ?? throw new NotFoundException("SubscriptionPlan", sub.PlanCode);

        var price = plan.PriceFor(sub.BillingCycle);
        var invoiceNumber = await _invoiceNumbers.NextAsync(tenantId);

        var invoice = new SubscriptionInvoice(
            tenantId:        tenantId,
            subscriptionId:  sub.Id,
            invoiceNumber:   invoiceNumber,
            planCode:        plan.Code,
            periodStart:     sub.EndDate,
            periodEnd:       NextPeriodEnd(sub.EndDate, sub.BillingCycle),
            subtotal:        price,
            currency:        plan.Currency,
            dueDate:         DateTime.UtcNow.AddDays(7),
            reason:          $"Renewal — {plan.Code} {sub.BillingCycle}");

        _db.SubscriptionInvoices.Add(invoice);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Renewal invoice generated: Tenant={TenantId}, Invoice={Number}, Amount={Amount} {Currency}",
            tenantId, invoice.InvoiceNumber, invoice.Total, invoice.Currency);

        return invoice;
    }

    public async Task<SubscriptionInvoice> GenerateProrationInvoiceAsync(
        Guid tenantId,
        Guid subscriptionId,
        decimal amount,
        string planCode,
        string currency,
        string reason)
    {
        if (amount <= 0)
            throw new BusinessRuleException("Proration amount must be positive.");

        var invoiceNumber = await _invoiceNumbers.NextAsync(tenantId);

        var invoice = new SubscriptionInvoice(
            tenantId:       tenantId,
            subscriptionId: subscriptionId,
            invoiceNumber:  invoiceNumber,
            planCode:       planCode,
            periodStart:    DateTime.UtcNow,
            periodEnd:      DateTime.UtcNow,
            subtotal:       amount,
            currency:       currency,
            dueDate:        DateTime.UtcNow.AddDays(7),
            reason:         reason);

        _db.SubscriptionInvoices.Add(invoice);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Proration invoice generated: Tenant={TenantId}, Invoice={Number}, Amount={Amount} {Currency}",
            tenantId, invoice.InvoiceNumber, amount, currency);

        return invoice;
    }

    // ---------------------------------------------------------------
    // Payment
    // ---------------------------------------------------------------

    public async Task<SubscriptionPayment> PayInvoiceAsync(Guid tenantId, Guid invoiceId, string paymentMethod)
    {
        var invoice = await LoadInvoiceAsync(tenantId, invoiceId);
        AssertPayable(invoice);

        if (string.IsNullOrWhiteSpace(paymentMethod))
            throw new BusinessRuleException("PaymentMethod is required.");

        // Manual methods: create a Pending payment, wait for SuperAdmin to confirm.
        if (ManualMethods.Contains(paymentMethod))
        {
            var manualPayment = new SubscriptionPayment(
                tenantId, invoice.Id, invoice.AmountDue, invoice.Currency, paymentMethod, paymentMethod);
            _db.SubscriptionPayments.Add(manualPayment);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Manual payment recorded as Pending: Tenant={TenantId}, Invoice={Number}, Method={Method}",
                tenantId, invoice.InvoiceNumber, paymentMethod);

            return manualPayment;
        }

        // Gateway-backed methods.
        var gateway = _gatewayFactory.GetByName(paymentMethod);
        var payment = new SubscriptionPayment(
            tenantId, invoice.Id, invoice.AmountDue, invoice.Currency, paymentMethod, paymentMethod);
        _db.SubscriptionPayments.Add(payment);
        await _db.SaveChangesAsync();

        var reference = invoice.InvoiceNumber;
        var result    = await gateway.ChargeAsync(invoice.AmountDue, invoice.Currency, reference);

        if (result.Success)
        {
            payment.MarkSucceeded(result.TransactionId);
            invoice.ApplyPayment(payment.Amount);

            // Advance the subscription period if this is a renewal invoice.
            await AdvanceSubscriptionOnPaidAsync(invoice);

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Subscription payment succeeded: Tenant={TenantId}, Invoice={Number}, Txn={Txn}",
                tenantId, invoice.InvoiceNumber, result.TransactionId);
        }
        else
        {
            payment.MarkFailed(result.Error ?? "Unknown gateway failure");
            await _db.SaveChangesAsync();

            _logger.LogWarning(
                "Subscription payment failed: Tenant={TenantId}, Invoice={Number}, Reason={Reason}",
                tenantId, invoice.InvoiceNumber, result.Error);
        }

        return payment;
    }

    public async Task<SubscriptionPayment> RecordManualPaymentAsync(
        Guid tenantId,
        Guid invoiceId,
        string paymentMethod,
        string? externalRef)
    {
        var invoice = await LoadInvoiceAsync(tenantId, invoiceId);
        AssertPayable(invoice);

        var payment = new SubscriptionPayment(
            tenantId, invoice.Id, invoice.AmountDue, invoice.Currency,
            string.IsNullOrWhiteSpace(paymentMethod) ? "BankTransfer" : paymentMethod,
            "Manual");

        payment.MarkSucceeded(externalRef);
        _db.SubscriptionPayments.Add(payment);

        invoice.ApplyPayment(payment.Amount);

        await AdvanceSubscriptionOnPaidAsync(invoice);

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Manual payment marked paid by SuperAdmin: Tenant={TenantId}, Invoice={Number}, Ref={Ref}",
            tenantId, invoice.InvoiceNumber, externalRef);

        return payment;
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private async Task<SubscriptionInvoice> LoadInvoiceAsync(Guid tenantId, Guid invoiceId)
    {
        return await _db.SubscriptionInvoices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.TenantId == tenantId)
            ?? throw new NotFoundException("SubscriptionInvoice", invoiceId);
    }

    private static void AssertPayable(SubscriptionInvoice invoice)
    {
        if (invoice.Status == InvoiceStatus.Paid)
            throw new BusinessRuleException("Invoice is already paid.");
        if (invoice.Status == InvoiceStatus.Void)
            throw new BusinessRuleException("Invoice is void.");
        if (invoice.Status == InvoiceStatus.Refunded)
            throw new BusinessRuleException("Invoice was refunded.");
        if (invoice.AmountDue <= 0)
            throw new BusinessRuleException("No amount due on this invoice.");
    }

    /// <summary>If the paid invoice covers a renewal period, advance the subscription dates.</summary>
    private async Task AdvanceSubscriptionOnPaidAsync(SubscriptionInvoice invoice)
    {
        if (invoice.Status != InvoiceStatus.Paid) return;
        if (!invoice.Reason.StartsWith("Renewal", StringComparison.OrdinalIgnoreCase)) return;

        var sub = await _db.TenantSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == invoice.SubscriptionId);
        if (sub == null) return;

        sub.RenewToNextPeriod();
    }

    private static DateTime NextPeriodEnd(DateTime from, BillingCycle cycle) =>
        cycle == BillingCycle.Yearly ? from.AddYears(1) : from.AddMonths(1);
}
