using RetailSuite.Shared;

namespace RetailSuite.Modules.Orders.Entities;

public class Customer : TenantEntity
{
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }

    private Customer() { }

    public Customer(string firstName, string lastName, string? email, string? phone)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        Phone = phone;
    }

    public string FullName => $"{FirstName} {LastName}";
}