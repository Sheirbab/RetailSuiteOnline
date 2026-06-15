namespace RetailSuite.Infrastructure.Modules.Customer.Dtos;


public class RegisterCustomerRequest
{
    public string Email { get; set; }
    public string Password { get; set; }

    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string? Phone { get; set; }
}