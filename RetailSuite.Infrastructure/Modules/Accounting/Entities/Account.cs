using RetailSuite.Shared;

namespace RetailSuite.Modules.Accounting.Entities;

public class Account : TenantEntity
{
    public string Code { get; private set; }
    public string Name { get; private set; }
    public AccountType AccountType { get; private set; }

    private Account() { }

    public Account(string code, string name, AccountType type)
    {
        Code = code;
        Name = name;
        AccountType = type;
    }
}