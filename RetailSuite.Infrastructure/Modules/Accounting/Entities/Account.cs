using RetailSuite.Shared;

namespace RetailSuite.Modules.Accounting.Entities;

/// <summary>
/// A Chart-of-Accounts row. Each tenant has its own set of accounts; the
/// TenantDefaultsSeeder populates the standard set (1000 Cash, 1100 Inventory,
/// 1200 AR, 2000 Tax Payable, 4000 Revenue, 5000 COGS, …) on tenant create.
/// </summary>
public class Account : TenantEntity
{
    public string      Code        { get; private set; } = string.Empty;
    public string      Name        { get; private set; } = string.Empty;
    public AccountType AccountType { get; private set; }
    public bool        IsActive    { get; private set; } = true;

    private Account() { }

    public Account(string code, string name, AccountType type)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code required.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required.");
        Code        = code.Trim();
        Name        = name.Trim();
        AccountType = type;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required.");
        Name = name.Trim();
    }

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code required.");
        Code = code.Trim();
    }

    public void SetType(AccountType type) => AccountType = type;

    public void Activate()   => IsActive = true;
    public void Deactivate() => IsActive = false;
}
