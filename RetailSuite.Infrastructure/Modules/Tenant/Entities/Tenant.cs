namespace RetailSuite.Infrastructure.Modules.Tenant.Entities;

public class Tenant
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Name { get; private set; } = string.Empty;
    public string Subdomain { get; private set; } = string.Empty;

    /// <summary>One of <see cref="TenantStatus"/> values.</summary>
    public string Status { get; private set; } = TenantStatus.PendingVerification;

    /// <summary>Email that receives billing / subscription notifications. May differ from admin user email.</summary>
    public string? BillingEmail { get; private set; }

    /// <summary>ISO 3166-1 alpha-2 country code. Drives currency, tax rules, available payment methods.</summary>
    public string CountryCode { get; private set; } = "PK";

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private Tenant() { }

    public Tenant(string name, string subdomain)
    {
        Name      = name;
        Subdomain = subdomain;
    }

    public Tenant(string name, string subdomain, string? billingEmail, string countryCode = "PK")
    {
        Name         = name;
        Subdomain    = subdomain;
        BillingEmail = billingEmail;
        CountryCode  = string.IsNullOrWhiteSpace(countryCode) ? "PK" : countryCode.ToUpperInvariant();
    }

    public void Update(string name, string subdomain)
    {
        Name      = name;
        Subdomain = subdomain;
    }

    public void SetStatus(string status) => Status = status;

    public void SetBillingEmail(string? email) => BillingEmail = email;

    public void SetCountryCode(string code)
    {
        CountryCode = string.IsNullOrWhiteSpace(code) ? "PK" : code.ToUpperInvariant();
    }
}
