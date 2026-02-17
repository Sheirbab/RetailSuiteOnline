using RetailSuite.Shared;

namespace RetailSuite.Modules.Accounting.Entities;

public class JournalEntryLine : TenantEntity
{
    public Guid JournalEntryId { get; private set; }
    public Guid AccountId { get; private set; }

    public decimal DebitAmount { get; private set; }
    public decimal CreditAmount { get; private set; }

    private JournalEntryLine() { }

    public JournalEntryLine(
        Guid journalEntryId,
        Guid accountId,
        decimal debit,
        decimal credit)
    {
        if (debit > 0 && credit > 0)
            throw new Exception("Line cannot have both debit and credit.");

        JournalEntryId = journalEntryId;
        AccountId = accountId;
        DebitAmount = debit;
        CreditAmount = credit;
    }
}