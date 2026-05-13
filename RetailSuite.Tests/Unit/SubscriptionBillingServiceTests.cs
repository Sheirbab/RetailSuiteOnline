using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Subscriptions.Entities;
using RetailSuite.Infrastructure.Modules.Subscriptions.Services;
using RetailSuite.Infrastructure.Payments;
using RetailSuite.Shared;

namespace RetailSuite.Tests.Unit;

/// <summary>
/// Verifies the billing service: renewal invoice creation, manual mark-paid,
/// and that gateway-routed payments call the right gateway and mark the invoice paid.
/// </summary>
public class SubscriptionBillingServiceTests
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

    private static (TenantSubscription sub, SubscriptionPlan plan) SeedSubscription(
        RetailDbContext db,
        decimal monthlyPrice)
    {
        var plan = new SubscriptionPlan("STARTER", "Starter", "", monthlyPrice, monthlyPrice * 10, trialDays: 0);
        db.SubscriptionPlans.Add(plan);
        db.SaveChanges();

        var tenantId = Guid.NewGuid();
        var sub = new TenantSubscription(tenantId, plan, BillingCycle.Monthly);
        db.TenantSubscriptions.Add(sub);
        db.SaveChanges();

        return (sub, plan);
    }

    private static SubscriptionBillingService NewService(
        RetailDbContext db,
        IPaymentGatewayFactory? factory = null)
    {
        var subSvc       = new SubscriptionService(db, NullLogger<SubscriptionService>.Instance);
        var invoiceNums  = new InvoiceNumberGenerator(db);
        var gateway      = factory ?? new Mock<IPaymentGatewayFactory>().Object;
        return new SubscriptionBillingService(
            db, invoiceNums, gateway, subSvc, NullLogger<SubscriptionBillingService>.Instance);
    }

    [Fact]
    public async Task GenerateRenewalInvoiceAsync_CreatesOpenInvoiceWithCorrectAmount()
    {
        await using var db = NewDb();
        var (sub, plan)    = SeedSubscription(db, monthlyPrice: 2_500m);
        var service        = NewService(db);

        var invoice = await service.GenerateRenewalInvoiceAsync(sub.TenantId, sub.Id);

        Assert.Equal(InvoiceStatus.Open, invoice.Status);
        Assert.Equal(2_500m, invoice.Total);
        Assert.Equal(0m, invoice.TaxAmount);    // tax-inclusive
        Assert.Equal("STARTER", invoice.PlanCode);
        Assert.Equal(0m, invoice.AmountPaid);
        Assert.Equal(2_500m, invoice.AmountDue);
        Assert.StartsWith("Renewal", invoice.Reason);
    }

    [Fact]
    public async Task GenerateProrationInvoiceAsync_RejectsZeroOrNegative()
    {
        await using var db = NewDb();
        var (sub, _) = SeedSubscription(db, 2_500m);
        var service  = NewService(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.GenerateProrationInvoiceAsync(sub.TenantId, sub.Id, 0m, "PRO", "PKR", "Upgrade"));
    }

    [Fact]
    public async Task PayInvoiceAsync_BankTransfer_RecordsPendingPayment()
    {
        await using var db = NewDb();
        var (sub, _)       = SeedSubscription(db, 2_500m);
        var service        = NewService(db);

        var invoice = await service.GenerateRenewalInvoiceAsync(sub.TenantId, sub.Id);
        var payment = await service.PayInvoiceAsync(sub.TenantId, invoice.Id, "BankTransfer");

        Assert.Equal(SubscriptionPaymentStatus.Pending, payment.Status);
        Assert.Equal("BankTransfer", payment.PaymentMethod);

        // Invoice is NOT yet paid because manual methods require confirmation.
        var refreshed = await db.SubscriptionInvoices.FirstAsync(i => i.Id == invoice.Id);
        Assert.Equal(InvoiceStatus.Open, refreshed.Status);
    }

    [Fact]
    public async Task PayInvoiceAsync_GatewaySuccess_MarksInvoicePaidAndPaymentSucceeded()
    {
        await using var db = NewDb();
        var (sub, _)       = SeedSubscription(db, 2_500m);

        var fakeGateway = new Mock<IPaymentGateway>();
        fakeGateway
            .Setup(g => g.ChargeAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new PaymentResult(true, "TXN-OK-123", null));

        var factory = new Mock<IPaymentGatewayFactory>();
        factory.Setup(f => f.GetByName(It.IsAny<string>())).Returns(fakeGateway.Object);

        var service = NewService(db, factory.Object);

        var invoice = await service.GenerateRenewalInvoiceAsync(sub.TenantId, sub.Id);
        var payment = await service.PayInvoiceAsync(sub.TenantId, invoice.Id, "Stripe");

        Assert.Equal(SubscriptionPaymentStatus.Succeeded, payment.Status);
        Assert.Equal("TXN-OK-123", payment.ProviderTxnRef);

        var refreshed = await db.SubscriptionInvoices.FirstAsync(i => i.Id == invoice.Id);
        Assert.Equal(InvoiceStatus.Paid, refreshed.Status);
        Assert.Equal(refreshed.Total, refreshed.AmountPaid);
    }

    [Fact]
    public async Task PayInvoiceAsync_GatewayFailure_RecordsFailedPaymentInvoiceStillOpen()
    {
        await using var db = NewDb();
        var (sub, _)       = SeedSubscription(db, 2_500m);

        var fakeGateway = new Mock<IPaymentGateway>();
        fakeGateway
            .Setup(g => g.ChargeAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new PaymentResult(false, string.Empty, "Insufficient funds"));

        var factory = new Mock<IPaymentGatewayFactory>();
        factory.Setup(f => f.GetByName(It.IsAny<string>())).Returns(fakeGateway.Object);

        var service = NewService(db, factory.Object);

        var invoice = await service.GenerateRenewalInvoiceAsync(sub.TenantId, sub.Id);
        var payment = await service.PayInvoiceAsync(sub.TenantId, invoice.Id, "JazzCash");

        Assert.Equal(SubscriptionPaymentStatus.Failed, payment.Status);
        Assert.Contains("Insufficient", payment.FailureReason);

        var refreshed = await db.SubscriptionInvoices.FirstAsync(i => i.Id == invoice.Id);
        Assert.Equal(InvoiceStatus.Open, refreshed.Status);
    }

    [Fact]
    public async Task RecordManualPaymentAsync_MarksInvoicePaid()
    {
        await using var db = NewDb();
        var (sub, _)       = SeedSubscription(db, 2_500m);
        var service        = NewService(db);

        var invoice = await service.GenerateRenewalInvoiceAsync(sub.TenantId, sub.Id);
        var payment = await service.RecordManualPaymentAsync(
            sub.TenantId, invoice.Id, "BankTransfer", "REF-ABC-001");

        Assert.Equal(SubscriptionPaymentStatus.Succeeded, payment.Status);
        Assert.Equal("REF-ABC-001", payment.ProviderTxnRef);
        Assert.Equal("Manual", payment.Provider);

        var refreshed = await db.SubscriptionInvoices.FirstAsync(i => i.Id == invoice.Id);
        Assert.Equal(InvoiceStatus.Paid, refreshed.Status);
        Assert.Equal(refreshed.Total, refreshed.AmountPaid);
    }

    [Fact]
    public async Task PayInvoiceAsync_RejectsAlreadyPaidInvoice()
    {
        await using var db = NewDb();
        var (sub, _)       = SeedSubscription(db, 2_500m);
        var service        = NewService(db);

        var invoice = await service.GenerateRenewalInvoiceAsync(sub.TenantId, sub.Id);
        await service.RecordManualPaymentAsync(sub.TenantId, invoice.Id, "BankTransfer", null);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.PayInvoiceAsync(sub.TenantId, invoice.Id, "BankTransfer"));
    }
}
