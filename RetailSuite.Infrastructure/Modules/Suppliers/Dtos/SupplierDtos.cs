using RetailSuite.Infrastructure.Modules.Suppliers.Entities;

namespace RetailSuite.Infrastructure.Modules.Suppliers.Dtos;

public record SupplierResponse(
    Guid    Id,
    string  Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? Address,
    string? Notes,
    bool    IsActive,
    DateTime CreatedAt);

public class CreateSupplierRequest
{
    public string  Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
}

public class UpdateSupplierRequest
{
    public string? Name { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool?   IsActive { get; set; }
}

public static class SupplierMappers
{
    public static SupplierResponse ToResponse(this Supplier s) =>
        new(s.Id, s.Name, s.ContactPerson, s.Phone, s.Email, s.Address,
            s.Notes, s.IsActive, s.CreatedAt);
}
