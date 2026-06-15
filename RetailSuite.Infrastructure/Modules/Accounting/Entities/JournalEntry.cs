using RetailSuite.Shared;

namespace RetailSuite.Modules.Accounting.Entities;

public class JournalEntry : TenantEntity
{
    public string? ReferenceId { get; private set; }
    public string Description { get; private set; }
    public bool IsManual { get; private set; }

    private readonly List<JournalEntryLine> _lines = new();
    public IReadOnlyCollection<JournalEntryLine> Lines => _lines;

    private JournalEntry() { }

    public JournalEntry(string? referenceId, string description)
    {
        ReferenceId = referenceId;
        Description = description;
    }

    public void AddLine(JournalEntryLine line)
    {
        _lines.Add(line);
    }
}