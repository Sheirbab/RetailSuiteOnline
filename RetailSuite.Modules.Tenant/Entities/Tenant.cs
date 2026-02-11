
using RetailSuite.Shared;

namespace RetailSuite.Modules.Tenant.Entities;

public class Tenant : BaseEntity
{
    public string Name { get; private set; } = default!;
    public string Subdomain { get; private set; } = default!;
    public string Status { get; private set; } = "Active";

    private Tenant() { }

    public Tenant(string name, string subdomain)
    {
        Name = name;
        Subdomain = subdomain;
    }
}
