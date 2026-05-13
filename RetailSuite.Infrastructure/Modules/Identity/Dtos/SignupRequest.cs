namespace RetailSuite.Infrastructure.Modules.Identity.Dtos;

public class SignupRequest
{
    public string TenantName { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Optional — billing notifications go here. Defaults to admin email if omitted.</summary>
    public string? BillingEmail { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code. Defaults to "PK".</summary>
    public string? CountryCode { get; set; }

    /// <summary>Subscription plan code to start on. Defaults to "FREE".</summary>
    public string? PlanCode { get; set; }

    /// <summary>Billing cycle for paid plans. "Monthly" or "Yearly". Defaults to "Monthly".</summary>
    public string? BillingCycle { get; set; }
}
