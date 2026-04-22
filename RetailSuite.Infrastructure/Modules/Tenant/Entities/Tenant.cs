namespace RetailSuite.Infrastructure.Modules.Tenant.Entities;

public class Tenant
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Name { get; private set; }
    public string Subdomain { get; private set; }
    public string Status { get; private set; } = "Active";

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private Tenant() { }

    public Tenant(string name, string subdomain)
    {
        Name      = name;
        Subdomain = subdomain;
    }

    public void Update(string name, string subdomain)
    {
        Name      = name;
        Subdomain = subdomain;
    }

    public void SetStatus(string status) => Status = status;
}