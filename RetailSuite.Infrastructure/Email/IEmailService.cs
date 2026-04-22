namespace RetailSuite.Infrastructure.Email;

/// <summary>Abstraction for sending transactional emails.</summary>
public interface IEmailService
{
    /// <summary>Send an HTML email. Failures are logged but never propagate to the caller.</summary>
    Task SendAsync(string to, string subject, string htmlBody);
}
