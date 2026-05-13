using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Email;
using RetailSuite.Infrastructure.Modules.Subscriptions.Entities;
using RetailSuite.Infrastructure.Modules.Tenant.Entities;
using RetailSuite.Infrastructure.Payments;
using RetailSuite.Shared;

namespace RetailSuite.Tests.Unit;

/// <summary>
/// Verifies the SubscriptionPaymentReconciler closes out pending payments,
/// applies amounts to invoices, restores tenant status, and is idempotent.
/// </summary>
public class SubscriptionPaymentReconcilerTests
{
    private static RetailDbContext NewDb()
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<RetailDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RetailDbContext(options, tenantContext.Object);
    }

    private static (Guid tenantId, SubscriptionInvoice invoice, SubscriptionPayment payment)
        SeedPendingPayment(RetailDbContext db, decimal amount = 2_500m, string txnRef = "TXN-PENDING-1")
    {
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant("Test Tenant", $"tenant-{Guid.NewGuid():N}", "billing@test.local", "PK"));
        db.SaveChanges();
        var tenantRow = db.Tenants.OrderByDescending(t => t.CreatedAt).First();
        // Use the seeded tenant's id for everything else so middleware/queries line up.
        tenantId = tenantRow.Id;

        var invoice = new SubscriptionInvoice(
            tenantId,
            subscriptionId: Guid.NewGuid(),
            invoiceNumber:  "INV-202605-0001",
            planCode:       "STARTER",
            periodStart:    DateTime.UtcNow,
            periodEnd:      DateTime.UtcNow.AddMonths(1),
            subtotal:       amount,
            currency:       "PKR",
            dueDate:        DateTime.UtcNow.AddDays(7),
            reason:         "Renewal — STARTER Monthly");
        db.SubscriptionInvoices.Add(invoice);

        var payment = new SubscriptionPayment(
            tenantId, invoice.Id, amount, "PKR", "JazzCash", "JazzCash");
        // ProviderTxnRef is set when the gateway charge call completes — simulate that.
        // For testing we'll match on whichever ProviderTxnRef the reconciler is given.
        // The reconciler also matches Pending payments without a ref, so this is OK as null too.
        db.SubscriptionPayments.Add(payment);

        db.SaveChanges();
        return (tenantId, invoice, payment);
    }

    private static SubscriptionPaymentReconciler NewReconciler(RetailDbContext db, INotificationService? notify = null) =>
        new(db,
            notify ?? new Mock<INotificationService>().Object,
            NullLogger<SubscriptionPaymentReconciler>.Instance);

    [Fact]
    public async Task ReconcileAsync_Success_MarksPaymentAndInvoicePaid()
    {
        await using var db = NewDb();
        var (tenantId, invoice, payment) = SeedPendingPayment(db);

        var reconciler = NewReconciler(db);

        var result = await reconciler.ReconcileAsync(
            providerTxnRef: "TXN-FROM-GATEWAY",
            succeeded:      true,
            amount:         invoice.Total);

        Assert.True(result.Reconciled);
        Assert.Equal(payment.Id, result.SubscriptionPaymentId);
        Assert.Equal(invoice.Id, result.InvoiceId);

        var paymentAfter = await db.SubscriptionPayments.AsNoTracking().FirstAsync(p => p.Id == payment.Id);
        Assert.Equal(SubscriptionPaymentStatus.Succeeded, paymentAfter.Status);
        Assert.Equal("TXN-FROM-GATEWAY", paymentAfter.ProviderTxnRef);

        var invoiceAfter = await db.SubscriptionInvoices.AsNoTracking().FirstAsync(i => i.Id == invoice.Id);
        Assert.Equal(InvoiceStatus.Paid, invoiceAfter.Status);
        Assert.Equal(invoiceAfter.Total, invoiceAfter.AmountPaid);
    }

    [Fact]
    public async Task ReconcileAsync_Failure_MarksPaymentFailed_InvoiceStaysOpen()
    {
        await using var db = NewDb();
        var (_, invoice, payment) = SeedPendingPayment(db);

        var reconciler = NewReconciler(db);

        var result = await reconciler.ReconcileAsync(
            providerTxnRef: "TXN-FROM-GATEWAY-FAIL",
            succeeded:      false,
            amount:         invoice.Total,
            failureReason:  "Customer declined OTP");

        Assert.True(result.Reconciled);
        Assert.Contains("OTP", result.Reason);

        var paymentAfter = await db.SubscriptionPayments.AsNoTracking().FirstAsync(p => p.Id == payment.Id);
        Assert.Equal(SubscriptionPaymentStatus.Failed, paymentAfter.Status);
        Assert.Contains("OTP", paymentAfter.FailureReason);

        var invoiceAfter = await db.SubscriptionInvoices.AsNoTracking().FirstAsync(i => i.Id == invoice.Id);
        Assert.Equal(InvoiceStatus.Open, invoiceAfter.Status);
        Assert.Equal(0m, invoiceAfter.AmountPaid);
    }

    [Fact]
    public async Task ReconcileAsync_Idempotent_WhenPaymentAlreadySucceeded()
    {
        await using var db = NewDb();
        var (_, invoice, payment) = SeedPendingPayment(db);
        var reconciler = NewReconciler(db);

        var first  = await reconciler.ReconcileAsync("TXN-OK", true, invoice.Total);
        var second = await reconciler.ReconcileAsync("TXN-OK", true, invoice.Total);

        Assert.True(first.Reconciled);
        Assert.True(second.Reconciled);

        // Only ONE payment row, only one invoice paid amount.
        var payments = await db.SubscriptionPayments.AsNoTracking()
            .Where(p => p.InvoiceId == invoice.Id).ToListAsync();
        Assert.Single(payments);
        var invoiceAfter = await db.SubscriptionInvoices.AsNoTracking().FirstAsync(i => i.Id == invoice.Id);
        Assert.Equal(invoiceAfter.Total, invoiceAfter.AmountPaid);
    }

    [Fact]
    public async Task ReconcileAsync_RestoresSuspendedTenantToActive()
    {
        await using var db = NewDb();
        var (tenantId, invoice, _) = SeedPendingPayment(db);

        // Push the tenant into Suspended (simulating prior non-payment).
        var tenant = await db.Tenants.FirstAsync(t => t.Id == tenantId);
        tenant.SetStatus(TenantStatus.Suspended);
        await db.SaveChangesAsync();

        var reconciler = NewReconciler(db);
        var result = await reconciler.ReconcileAsync("TXN-OK", true, invoice.Total);

        Assert.True(result.Reconciled);

        var tenantAfter = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId);
        Assert.Equal(TenantStatus.Active, tenantAfter.Status);
    }

    [Fact]
    public async Task ReconcileAsync_NoMatchingPayment_ReturnsNotReconciled()
    {
        await using var db = NewDb();
        var reconciler = NewReconciler(db);

        var result = await reconciler.ReconcileAsync("UNKNOWN-TXN", true, 1_000m);

        Assert.False(result.Reconciled);
        Assert.Null(result.SubscriptionPaymentId);
        Assert.Contains("No matching", result.Reason);
    }
}
