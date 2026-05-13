namespace RetailSuite.Infrastructure.Modules.Subscriptions.Services;

/// <summary>
/// Configuration for the subscription billing pipeline.
/// Bind from appsettings.json under "Billing" section.
/// </summary>
public class BillingOptions
{
    public const string Section = "Billing";

    /// <summary>Public base URL used to build "Pay invoice" links in emails.</summary>
    public string PublicBaseUrl { get; set; } = "https://app.retailsuite.local";

    /// <summary>Days after issue before the invoice is marked Overdue. Default 7.</summary>
    public int OverdueAfterDays { get; set; } = 7;

    /// <summary>Days past due before the tenant is moved to PastDue status. Default 7.</summary>
    public int PastDueAfterDays { get; set; } = 7;

    /// <summary>Days past due before the tenant is suspended. Default 14 (cumulative — i.e. 7 days after PastDue).</summary>
    public int SuspendAfterDays { get; set; } = 14;

    /// <summary>Renewal job cadence in minutes. Default 60 (hourly). Set to 1440 for daily.</summary>
    public int RenewalJobIntervalMinutes { get; set; } = 60;

    /// <summary>Pause the renewal hosted service entirely (useful for tests / dev).</summary>
    public bool RenewalJobEnabled { get; set; } = true;
}
