using Microsoft.Extensions.Logging;
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

        // Round each side to 2 decimal places (matches the DB column precision)
        // before comparing so tiny in-memory drift doesn't fail the check.
        var totalDebit = Math.Round(lines.Sum(x => x.debit), 2, MidpointRounding.AwayFromZero);
        var totalCredit = Math.Round(lines.Sum(x => x.credit), 2, MidpointRounding.AwayFromZero);

        if (totalDebit != totalCredit)
            throw new Exception(
                $"Journal entry '{description}' not balanced. " +
                $"Debits = {totalDebit:N2}, Credits = {totalCredit:N2}, Diff = {(totalDebit - totalCredit):N2}.");

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