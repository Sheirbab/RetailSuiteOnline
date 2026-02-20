using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Customer.Entities;

public class Customer : TenantEntity
{
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public Guid UserId { get; private set; }
    private Customer() { }

    public Customer(Guid userId, string firstName, string lastName, string? email, string? phone)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        Phone = phone;
        UserId = userId;
    }

    public string FullName => $"{FirstName} {LastName}";
}