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

    /// <summary>
    /// Flag this entry as a manual adjustment (as opposed to one auto-posted by a
    /// business action like a POS sale). Manual entries are highlighted in the GL
    /// explorer so auditors can filter them.
    /// </summary>
    public void MarkManual() => IsManual = true;
}