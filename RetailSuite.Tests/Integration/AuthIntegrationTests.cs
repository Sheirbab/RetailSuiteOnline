// Integration tests require a running SQL Server database and are excluded from the default test run.
// They serve as a template for full end-to-end verification.
// To run: dotnet test --filter "Category=Integration"

namespace RetailSuite.Tests.Integration;

/// <summary>
/// Integration tests for the Auth controller.
/// These tests are marked as Explicit and will not run in standard CI unless enabled.
/// To make these runnable, configure a test SQL Server instance in the environment
/// and add Microsoft.AspNetCore.Mvc.Testing to the test project.
/// </summary>
public class AuthIntegrationTests
{
    // Placeholder — full implementation requires WebApplicationFactory<Program>
    // and an accessible SQL Server (or SQLite) for EF Core.

    [Fact(Skip = "Integration test — requires running infrastructure. Remove Skip to enable.")]
    public async Task Signup_ReturnsJwtToken()
    {
        // Arrange
        // var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => { ... });
        // var client  = factory.CreateClient();

        // Act
        // var res = await client.PostAsJsonAsync("/api/auth/signup", new { ... });

        // Assert
        // res.EnsureSuccessStatusCode();
        await Task.CompletedTask;
    }

    [Fact(Skip = "Integration test — requires running infrastructure. Remove Skip to enable.")]
    public async Task Login_WithWrongPassword_Returns401()
    {
        await Task.CompletedTask;
    }
}
