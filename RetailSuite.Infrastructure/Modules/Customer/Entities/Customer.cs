using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Customer.Entities;

public class Customer : TenantEntity
{
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>Pakistani national ID number — captured for B2B sales above the FBR threshold.</summary>
    public string? Cnic { get; private set; }

    /// <summary>Customer segment — drives reporting and (later) tier-based perks.</summary>
    public CustomerGroup Group { get; private set; } = CustomerGroup.Regular;

    /// <summary>True if the customer has opted in to receive marketing email / SMS.</summary>
    public bool MarketingConsent { get; private set; }

    /// <summary>Optional date of birth — used for birthday promos.</summary>
    public DateTime? DateOfBirth { get; private set; }

    /// <summary>Free-text staff notes about the customer (preferences, do-not-contact reasons, etc.).</summary>
    public string? Notes { get; private set; }

    private Customer() { }

    public Customer(Guid userId, string firstName, string lastName, string? email, string? phone)
    {
        FirstName = firstName;
        LastName  = lastName;
        Email     = email;
        Phone     = phone;
        UserId    = userId;
    }

    public string FullName => $"{FirstName} {LastName}";

    // ---- Mutators -----------------------------------------------------

    public void UpdateContact(string? email, string? phone)
    {
        Email = email;
        Phone = phone;
    }

    public void SetCnic(string? cnic) => Cnic = cnic;

    public void SetGroup(CustomerGroup group) => Group = group;

    public void SetMarketingConsent(bool consent) => MarketingConsent = consent;

    public void SetDateOfBirth(DateTime? dob) => DateOfBirth = dob;

    public void SetNotes(string? notes) => Notes = notes;

    public void Rename(string firstName, string lastName)
    {
        if (!string.IsNullOrWhiteSpace(firstName)) FirstName = firstName;
        if (!string.IsNullOrWhiteSpace(lastName))  LastName  = lastName;
    }
}
