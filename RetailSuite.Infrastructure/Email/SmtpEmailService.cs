using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;

namespace RetailSuite.Infrastructure.Email;

/// <summary>
/// Sends emails via SMTP (System.Net.Mail).
/// Configure in appsettings.json under the "Email" section.
/// Set Email:Host to "" to disable sending (dev/test mode).
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        var host = _config["Email:Host"];

        // Skip sending if SMTP is not configured (dev/test)
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogInformation("[Email skipped — no SMTP host configured] To: {To} | Subject: {Subject}", to, subject);
            return;
        }

        try
        {
            var port      = int.TryParse(_config["Email:Port"],    out var p) ? p : 587;
            var enableSsl = bool.TryParse(_config["Email:EnableSsl"], out var ssl) && ssl;
            var from      = _config["Email:From"]     ?? "noreply@retailsuite.com";
            var username  = _config["Email:Username"] ?? string.Empty;
            var password  = _config["Email:Password"] ?? string.Empty;

            using var client = new SmtpClient(host, port)
            {
                EnableSsl   = enableSsl,
                Credentials = string.IsNullOrEmpty(username)
                    ? null
                    : new NetworkCredential(username, password)
            };

            var message = new MailMessage(from, to, subject, htmlBody) { IsBodyHtml = true };
            await client.SendMailAsync(message);

            _logger.LogInformation("Email sent to {To}: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            // Email failure must never crash the application
            _logger.LogWarning(ex, "Failed to send email to {To}: {Subject}", to, subject);
        }
    }
}
