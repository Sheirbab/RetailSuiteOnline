using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using RetailSuite.Api.Controllers;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Customer.Model;
using RetailSuite.Infrastructure.Modules.Identity.Dtos;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Tests.Unit;

public class AuthControllerTests
{
    private static RetailDbContext CreateInMemoryDb()
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<RetailDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RetailDbContext(options, tenantContext.Object);
    }

    private static IConfiguration CreateConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "this_is_a_test_key_that_is_long_enough",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience"
            })
            .Build();

    [Fact]
    public async Task Login_ReturnsBadRequest_WhenEmailExistsInMultipleTenantsAndNoSubdomain()
    {
        await using var db = CreateInMemoryDb();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var email = "shared@test.com";
        var password = "Password123!";

        db.Users.Add(new User(tenantA, email, BCrypt.Net.BCrypt.HashPassword(password), UserRole.Admin));
        db.Users.Add(new User(tenantB, email, BCrypt.Net.BCrypt.HashPassword(password), UserRole.Admin));
        await db.SaveChangesAsync();

        var controller = new AuthController(db, CreateConfig());

        var result = await controller.Login(new LoginRequest
        {
            Email = email,
            Password = password
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<ApiResponse<string>>(badRequest.Value);
        Assert.False(response.Success);
        Assert.Contains("provide subdomain", response.Message, StringComparison.OrdinalIgnoreCase);
    }
}
