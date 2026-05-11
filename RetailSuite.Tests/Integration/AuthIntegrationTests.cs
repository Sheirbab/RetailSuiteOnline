using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RetailSuite.Tests.Integration;

public class AuthIntegrationTests
{
    [Fact]
    public async Task Signup_ReturnsJwtToken()
    {
        await using var factory = new ApiTestFactory();
        var client = factory.CreateClient();
        var subdomain = $"auth-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            tenantName = "Auth Test Store",
            subdomain,
            email = $"admin-{subdomain}@example.com",
            password = "Test@12345"
        });

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("data").GetString()));
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        await using var factory = new ApiTestFactory();
        var client = factory.CreateClient();
        var subdomain = $"login-{Guid.NewGuid():N}";
        var email = $"admin-{subdomain}@example.com";

        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            tenantName = "Login Test Store",
            subdomain,
            email,
            password = "Test@12345"
        });
        signup.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "Wrong@12345",
            subdomain
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
