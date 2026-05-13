namespace RetailSuite.Infrastructure.Modules.Identity.Services;

/// <summary>
/// Configuration for email-verification flow.
/// Bind from appsettings.json under "Verification" section.
/// </summary>
public class VerificationOptions
{
    public const string Section = "Verification";

    /// <summary>Public base URL used to build verification links emailed to users.</summary>
    /// <example>https://app.retailsuite.com</example>
    public string PublicBaseUrl { get; set; } = "https://app.retailsuite.local";

    /// <summary>How long a verification token stays valid, in hours.</summary>
    public int TokenTtlHours { get; set; } = 24;

    /// <summary>Minimum gap between consecutive resends, in seconds.</summary>
    public int ResendCooldownSeconds { get; set; } = 60;
}
