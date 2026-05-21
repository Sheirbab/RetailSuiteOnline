using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Customer.Entities;

/// <summary>
/// One mailing / delivery address belonging to a customer. A customer may have many
/// (e.g. home + office). Exactly one default-shipping and one default-billing address
/// should exist at a time — enforced by <see cref="Services.CustomerAddressService"/>.
/// </summary>
public class CustomerAddress : TenantEntity
{
    public Guid CustomerId { get; private set; }

    /// <summary>Friendly label — "Home", "Office", "Mom's place".</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>Recipient name on the parcel — defaults to the customer's full name but can override.</summary>
    public string RecipientName { get; private set; } = string.Empty;

    public string Line1 { get; private set; } = string.Empty;
    public string? Line2 { get; private set; }
    public string City { get; private set; } = string.Empty;
    public string? Province { get; private set; }
    public string? PostalCode { get; private set; }
    public string Country { get; private set; } = "PK";

    /// <summary>Phone for the courier to call — may differ from the customer's main phone.</summary>
    public string? Phone { get; private set; }

    public bool IsDefaultShipping { get; private set; }
    public bool IsDefaultBilling { get; private set; }

    /// <summary>Free-text instructions — "ring twice, gate code 1234".</summary>
    public string? DeliveryInstructions { get; private set; }

    private CustomerAddress() { }

    public CustomerAddress(
        Guid tenantId,
        Guid customerId,
        string label,
        string recipientName,
        string line1,
        string city,
        string country = "PK")
    {
        Id            = Guid.NewGuid();
        CreatedAt     = DateTime.UtcNow;
        TenantId      = tenantId;
        CustomerId    = customerId;
        Label         = string.IsNullOrWhiteSpace(label) ? "Address" : label.Trim();
        RecipientName = recipientName;
        Line1         = line1;
        City          = city;
        Country       = string.IsNullOrWhiteSpace(country) ? "PK" : country.ToUpperInvariant();
    }

    public void Update(
        string label, string recipientName,
        string line1, string? line2, string city, string? province, string? postalCode, string country,
        string? phone, string? deliveryInstructions)
    {
        Label                = string.IsNullOrWhiteSpace(label) ? Label : label.Trim();
        RecipientName        = recipientName;
        Line1                = line1;
        Line2                = line2;
        City                 = city;
        Province             = province;
        PostalCode           = postalCode;
        Country              = string.IsNullOrWhiteSpace(country) ? Country : country.ToUpperInvariant();
        Phone                = phone;
        DeliveryInstructions = deliveryInstructions;
    }

    public void MarkDefaultShipping() => IsDefaultShipping = true;
    public void UnmarkDefaultShipping() => IsDefaultShipping = false;
    public void MarkDefaultBilling()  => IsDefaultBilling = true;
    public void UnmarkDefaultBilling() => IsDefaultBilling = false;
}
