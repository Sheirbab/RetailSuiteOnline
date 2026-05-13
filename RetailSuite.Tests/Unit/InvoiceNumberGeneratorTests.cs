using Microsoft.EntityFrameworkCore;
using Moq;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Subscriptions.Entities;
using RetailSuite.Infrastructure.Modules.Subscriptions.Services;
using RetailSuite.Shared;

namespace RetailSuite.Tests.Unit;

/// <summary>
/// Verifies invoice numbers are sequential per tenant per month and unique.
/// </summary>
public class InvoiceNumberGeneratorTests
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

    [Fact]
    public async Task NextAsync_StartsAtOne_WhenNoExistingInvoices()
    {
        await using var db = NewDb();
        var gen = new InvoiceNumberGenerator(db);
        var tenantId = Guid.NewGuid();

        var number = await gen.NextAsync(tenantId);

        Assert.Matches(@"^INV-\d{6}-0001$", number);
    }

    [Fact]
    public async Task NextAsync_IncrementsPerTenant()
    {
        await using var db = NewDb();
        var gen = new InvoiceNumberGenerator(db);
        var tenantId = Guid.NewGuid();

        var first  = await gen.NextAsync(tenantId);
        await SeedInvoice(db, tenantId, first);

        var second = await gen.NextAsync(tenantId);
        await SeedInvoice(db, tenantId, second);

        var third  = await gen.NextAsync(tenantId);

        Assert.EndsWith("-0001", first);
        Assert.EndsWith("-0002", second);
        Assert.EndsWith("-0003", third);
    }

    [Fact]
    public async Task NextAsync_SequencesAreIsolatedBetweenTenants()
    {
        await using var db = NewDb();
        var gen = new InvoiceNumberGenerator(db);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var aFirst = await gen.NextAsync(tenantA);
        await SeedInvoice(db, tenantA, aFirst);

        var bFirst = await gen.NextAsync(tenantB);
        await SeedInvoice(db, tenantB, bFirst);

        var aSecond = await gen.NextAsync(tenantA);

        // tenantB still starts at 1 because its own sequence is empty until aFirst.
        Assert.EndsWith("-0001", aFirst);
        Assert.EndsWith("-0001", bFirst);
        Assert.EndsWith("-0002", aSecond);
    }

    private static async Task SeedInvoice(RetailDbContext db, Guid tenantId, string invoiceNumber)
    {
        var invoice = new SubscriptionInvoice(
            tenantId,
            subscriptionId: Guid.NewGuid(),
            invoiceNumber:  invoiceNumber,
            planCode:       "FREE",
            periodStart:    DateTime.UtcNow,
            periodEnd:      DateTime.UtcNow.AddMonths(1),
            subtotal:       100m,
            currency:       "PKR",
            dueDate:        DateTime.UtcNow.AddDays(7),
            reason:         "Test");
        db.SubscriptionInvoices.Add(invoice);
        await db.SaveChangesAsync();
    }
}
