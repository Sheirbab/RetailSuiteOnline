namespace RetailSuite.Modules.Identity.Dtos;

public class SignupRequest
{
    public string TenantName { get; set; }
    public string Subdomain { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
}
