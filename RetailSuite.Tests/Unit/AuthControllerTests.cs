using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RetailSuite.Api.Controllers;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Email;
using RetailSuite.Infrastructure.Modules.Customer.Model;
using RetailSuite.Infrastructure.Modules.Identity.Dtos;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Infrastructure.Modules.Identity.Services;
using RetailSuite.Infrastructure.Modules.Subscriptions.Services;
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
                ["Jwt:Key"]      = "this_is_a_test_key_that_is_long_enough",
                ["Jwt:Issuer"]   = "test-issuer",
                ["Jwt:Audience"] = "test-audience"
            })
            .Build();

    private static AuthController CreateController(RetailDbContext db)
    {
        var verifyOptions = Options.Create(new VerificationOptions
        {
            PublicBaseUrl         = "https://test.local",
            TokenTtlHours         = 24,
            ResendCooldownSeconds = 60
        });

        var tokenService  = new VerificationTokenService(
            db, verifyOptions, NullLogger<VerificationTokenService>.Instance);

        var notifications = new Mock<INotificationService>().Object;
        var subs          = new Mock<ISubscriptionService>().Object;

        return new AuthController(db, CreateConfig(), tokenService, notifications, subs, verifyOptions);
    }

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

        var controller = CreateController(db);

        var result = await controller.Login(new LoginRequest
        {
            Email    = email,
            Password = password
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response   = Assert.IsType<ApiResponse<string>>(badRequest.Value);
        Assert.False(response.Success);
        Assert.Contains("provide subdomain", response.Message, StringComparison.OrdinalIgnoreCase);
    }
}
