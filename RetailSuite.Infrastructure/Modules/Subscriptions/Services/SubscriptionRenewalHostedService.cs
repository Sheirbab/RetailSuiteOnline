using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RetailSuite.Infrastructure.Email;
using RetailSuite.Infrastructure.Modules.Subscriptions.Entities;
using RetailSuite.Infrastructure.Modules.Tenant.Entities;

namespace RetailSuite.Infrastructure.Modules.Subscriptions.Services;

/// <summary>
/// Background worker that drives subscription renewals + dunning.
/// Runs on a configurable interval (default hourly). Per-tick it:
///   1. Issues invoices for subscriptions whose NextBillingAt &lt;= now.
///   2. Marks Open invoices Overdue past their DueDate.
///   3. Sets tenant Status to PastDue / Suspended at configured grace points.
///   4. Sends dunning emails (overdue, suspended).
/// All work is idempotent — re-running a tick is safe.
/// </summary>
public class SubscriptionRenewalHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BillingOptions _options;
    private readonly ILogger<SubscriptionRenewalHostedService> _logger;

    public SubscriptionRenewalHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<BillingOptions> options,
        ILogger<SubscriptionRenewalHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options      = options.Value;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.RenewalJobEnabled)
        {
            _logger.LogInformation("Subscription renewal job disabled via configuration.");
            return;
        }

        // Initial small delay so app finishes warming up before the first scan.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.RenewalJobIntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Subscription renewal tick failed.");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db        = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        var billing   = scope.ServiceProvider.GetRequiredService<ISubscriptionBillingService>();
        var notify    = scope.ServiceProvider.GetRequiredService<INotificationService>();

        await IssueRenewalInvoicesAsync(db, billing, notify, ct);
        await MarkInvoicesOverdueAsync(db, notify, ct);
        await EnforceGraceAndSuspendAsync(db, notify, ct);
    }

    // ---------------------------------------------------------------
    // 1. Renewal invoice generation
    // ---------------------------------------------------------------
    private async Task IssueRenewalInvoicesAsync(
        RetailDbContext db,
        ISubscriptionBillingService billing,
        INotificationService notify,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var due = await db.TenantSubscriptions
            .IgnoreQueryFilters()
            .Where(s => !s.IsDeleted
                     && s.NextBillingAt <= now
                     && (s.Status == SubscriptionStatus.Active
                         || s.Status == SubscriptionStatus.Trialing))
            .ToListAsync(ct);

        foreach (var sub in due)
        {
            // Soft-cancel: don't issue a new invoice, just mark expired.
            if (sub.CancelAtPeriodEnd)
            {
                sub.MarkExpired();
                continue;
            }

            // Free plan: no invoice needed; just advance the period.
            if (sub.LastPrice <= 0)
            {
                sub.RenewToNextPeriod();
                continue;
            }

            // Skip if we've already issued an Open/Overdue invoice for this period.
            var alreadyIssued = await db.SubscriptionInvoices
                .IgnoreQueryFilters()
                .AnyAsync(i => i.SubscriptionId == sub.Id
                            && (i.Status == InvoiceStatus.Open || i.Status == InvoiceStatus.Overdue)
                            && i.PeriodStart >= sub.EndDate.AddDays(-1), ct);

            if (alreadyIssued) continue;

            try
            {
                var invoice = await billing.GenerateRenewalInvoiceAsync(sub.TenantId, sub.Id);

                var (toAddress, recipientName, tenantName) = await ResolveRecipientAsync(db, sub.TenantId, ct);
                if (!string.IsNullOrWhiteSpace(toAddress))
                {
                    await notify.SendInvoiceIssuedAsync(
                        toAddress, recipientName, tenantName,
                        invoice.InvoiceNumber, invoice.Total, invoice.Currency,
                        invoice.DueDate, BuildPayUrl(invoice.Id),
                        tenantId: sub.TenantId, invoiceId: invoice.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to generate renewal invoice for subscription {SubscriptionId}.",
                    sub.Id);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------
    // 2. Mark Open invoices Overdue past DueDate
    // ---------------------------------------------------------------
    private async Task MarkInvoicesOverdueAsync(
        RetailDbContext db,
        INotificationService notify,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var toMark = await db.SubscriptionInvoices
            .IgnoreQueryFilters()
            .Where(i => !i.IsDeleted
                     && i.Status == InvoiceStatus.Open
                     && i.DueDate < now)
            .ToListAsync(ct);

        foreach (var invoice in toMark)
        {
            invoice.MarkOverdue();

            var (toAddress, recipientName, tenantName) = await ResolveRecipientAsync(db, invoice.TenantId, ct);
            if (!string.IsNullOrWhiteSpace(toAddress))
            {
                await notify.SendInvoiceOverdueAsync(
                    toAddress, recipientName, tenantName,
                    invoice.InvoiceNumber, invoice.AmountDue, invoice.Currency,
                    invoice.DueDate, BuildPayUrl(invoice.Id),
                    tenantId: invoice.TenantId, invoiceId: invoice.Id);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------
    // 3. Move tenants through PastDue → Suspended based on overdue age
    // ---------------------------------------------------------------
    private async Task EnforceGraceAndSuspendAsync(
        RetailDbContext db,
        INotificationService notify,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var overdueInvoices = await db.SubscriptionInvoices
            .IgnoreQueryFilters()
            .Where(i => !i.IsDeleted && i.Status == InvoiceStatus.Overdue)
            .ToListAsync(ct);

        // Group oldest-overdue invoice per tenant — drives the decision.
        var oldestByTenant = overdueInvoices
            .GroupBy(i => i.TenantId)
            .Select(g => g.OrderBy(i => i.DueDate).First())
            .ToList();

        foreach (var invoice in oldestByTenant)
        {
            var daysOverdue = (now - invoice.DueDate).TotalDays;
            var tenant = await db.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == invoice.TenantId, ct);
            if (tenant == null) continue;

            if (daysOverdue >= _options.SuspendAfterDays && tenant.Status != TenantStatus.Suspended)
            {
                tenant.SetStatus(TenantStatus.Suspended);

                var (toAddress, recipientName, tenantName) = await ResolveRecipientAsync(db, tenant.Id, ct);
                if (!string.IsNullOrWhiteSpace(toAddress))
                {
                    await notify.SendTenantSuspendedAsync(
                        toAddress, recipientName, tenantName,
                        invoice.InvoiceNumber, BuildPayUrl(invoice.Id),
                        tenantId: tenant.Id, invoiceId: invoice.Id);
                }

                _logger.LogWarning(
                    "Tenant suspended for non-payment: Tenant={TenantId}, Invoice={Invoice}, DaysOverdue={Days}",
                    tenant.Id, invoice.InvoiceNumber, (int)daysOverdue);
            }
            else if (daysOverdue >= _options.PastDueAfterDays && tenant.Status == TenantStatus.Active)
            {
                tenant.SetStatus(TenantStatus.PastDue);

                _logger.LogInformation(
                    "Tenant moved to PastDue: Tenant={TenantId}, Invoice={Invoice}",
                    tenant.Id, invoice.InvoiceNumber);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private string BuildPayUrl(Guid invoiceId)
        => $"{_options.PublicBaseUrl.TrimEnd('/')}/billing/invoices/{invoiceId}/pay";

    private static async Task<(string toAddress, string recipientName, string tenantName)>
        ResolveRecipientAsync(RetailDbContext db, Guid tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        if (tenant == null) return (string.Empty, string.Empty, string.Empty);

        // Prefer BillingEmail; fall back to first admin user email.
        var to = tenant.BillingEmail;
        if (string.IsNullOrWhiteSpace(to))
        {
            to = await db.Users
                .IgnoreQueryFilters()
                .Where(u => u.TenantId == tenantId && !u.IsDeleted)
                .OrderBy(u => u.CreatedAt)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(ct);
        }

        return (to ?? string.Empty, to ?? tenant.Name, tenant.Name);
    }
}
