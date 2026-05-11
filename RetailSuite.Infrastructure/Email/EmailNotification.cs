using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Email;

/// <summary>
/// Audit log entry for every outbound transactional email.
/// Persisted regardless of SMTP success/failure so support can replay/troubleshoot.
/// </summary>
public class EmailNotification : TenantEntity
{
    public string ToAddress { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string TemplateKey { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;

    /// <summary>Pending | Sent | Failed</summary>
    public EmailStatus Status { get; private set; }

    public string? ErrorMessage { get; private set; }
    public DateTime? SentAt { get; private set; }

    /// <summary>Optional reference (e.g. OrderId, PaymentId) to correlate with business events.</summary>
    public string? RelatedEntityId { get; private set; }
    public string? RelatedEntityType { get; private set; }

    private EmailNotification() { }

    public EmailNotification(
        string toAddress,
        string subject,
        string templateKey,
        string body,
        string? relatedEntityType = null,
        string? relatedEntityId = null)
    {
        ToAddress = toAddress;
        Subject = subject;
        TemplateKey = templateKey;
        Body = body;
        Status = EmailStatus.Pending;
        RelatedEntityType = relatedEntityType;
        RelatedEntityId = relatedEntityId;
        CreatedAt = DateTime.UtcNow;
    }

    public void MarkSent()
    {
        Status = EmailStatus.Sent;
        SentAt = DateTime.UtcNow;
        ErrorMessage = null;
    }

    public void MarkFailed(string error)
    {
        Status = EmailStatus.Failed;
        ErrorMessage = error?.Length > 1000 ? error.Substring(0, 1000) : error;
    }
}

public enum EmailStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}
