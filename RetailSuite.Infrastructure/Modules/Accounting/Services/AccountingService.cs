using RetailSuite.Infrastructure;
using RetailSuite.Modules.Accounting.Entities;

namespace RetailSuite.Modules.Accounting.Services;

public class AccountingService
{
    private readonly RetailDbContext _db;

    public AccountingService(RetailDbContext db)
    {
        _db = db;
    }

    public async Task CreateJournalEntryAsync(
        string? referenceId,
        string description,
        List<(Guid accountId, decimal debit, decimal credit)> lines)
    {
        if (!lines.Any())
            throw new Exception("Journal entry must have lines.");

        var totalDebit = lines.Sum(x => x.debit);
        var totalCredit = lines.Sum(x => x.credit);

        if (totalDebit != totalCredit)
            throw new Exception("Journal entry not balanced.");

        var entry = new JournalEntry(referenceId, description);

        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync(); // Needed to generate entry.Id

        foreach (var line in lines)
        {
            var journalLine = new JournalEntryLine(
                entry.Id,
                line.accountId,
                line.debit,
                line.credit);

            _db.JournalEntryLines.Add(journalLine);
        }

        await _db.SaveChangesAsync();
    }
}