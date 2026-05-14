using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Suppliers.Entities;

/// <summary>
/// A vendor / wholesaler the tenant receives stock from. Linked to
/// <see cref="Receiving.Entities.ReceivingOrder"/> for purchase-order tracking.
/// Soft-deletes via the inherited <c>IsDeleted</c> flag.
/// </summary>
public class Supplier : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? ContactPerson { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Supplier() { }

    public Supplier(Guid tenantId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Supplier name is required.", nameof(name));

        Id        = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        TenantId  = tenantId;
        Name      = name.Trim();
    }

    public void UpdateContact(string? contactPerson, string? phone, string? email, string? address)
    {
        ContactPerson = contactPerson;
        Phone         = phone;
        Email         = email;
        Address       = address;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Supplier name is required.", nameof(name));
        Name = name.Trim();
    }

    public void SetNotes(string? notes) => Notes = notes;
    public void Activate()               => IsActive = true;
    public void Deactivate()             => IsActive = false;
}
