using Microsoft.EntityFrameworkCore;
using Moq;
using RetailSuite.Infrastructure;
using RetailSuite.Modules.Accounting.Entities;
using RetailSuite.Modules.Accounting.Services;
using RetailSuite.Shared;

namespace RetailSuite.Tests.Unit;

public class AccountingServiceTests
{
    private static RetailDbContext CreateInMemoryDb()
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(Guid.NewGuid());

        var options = new DbContextOptionsBuilder<RetailDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RetailDbContext(options, tenantContext.Object);
    }

    [Fact]
    public async Task CreateJournalEntry_ThrowsException_WhenUnbalanced()
    {
        await using var db = CreateInMemoryDb();
        var service = new AccountingService(db);

        await Assert.ThrowsAsync<Exception>(() =>
            service.CreateJournalEntryAsync("REF-001", "Test entry",
                new List<(Guid, decimal, decimal)>
                {
                    (Guid.NewGuid(), 100m, 0),   // debit 100
                    (Guid.NewGuid(), 0,    50m)   // credit 50 — NOT balanced
                }));
    }

    [Fact]
    public async Task CreateJournalEntry_SavesEntry_WhenBalanced()
    {
        await using var db = CreateInMemoryDb();
        var service = new AccountingService(db);

        var tenantId  = Guid.NewGuid();
        var account1  = new Account("1000", "Cash",    AccountType.Asset)   { TenantId = tenantId };
        var account2  = new Account("4000", "Revenue", AccountType.Revenue) { TenantId = tenantId };
        db.Accounts.AddRange(account1, account2);
        await db.SaveChangesAsync();

        await service.CreateJournalEntryAsync("REF-002", "Balanced entry",
            new List<(Guid, decimal, decimal)>
            {
                (account1.Id, 200m, 0),
                (account2.Id, 0,   200m)
            });

        Assert.Equal(1, await db.JournalEntries.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await db.JournalEntryLines.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task CreateJournalEntry_ThrowsException_WhenNoLines()
    {
        await using var db = CreateInMemoryDb();
        var service = new AccountingService(db);

        await Assert.ThrowsAsync<Exception>(() =>
            service.CreateJournalEntryAsync("REF-003", "Empty entry",
                new List<(Guid, decimal, decimal)>()));
    }
}
