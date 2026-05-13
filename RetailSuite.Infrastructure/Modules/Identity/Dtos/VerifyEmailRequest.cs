namespace RetailSuite.Infrastructure.Modules.Identity.Dtos;

public class VerifyEmailRequest
{
    public string Token { get; set; } = string.Empty;
}

public class ResendVerificationRequest
{
    public string Email { get; set; } = string.Empty;
    public string? Subdomain { get; set; }
}
