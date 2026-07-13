using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetailSuite.Infrastructure;

namespace RetailSuite.Tests.Integration;

public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"RetailSuiteTests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Program.cs throws at startup if ConnectionStrings:Default is missing.
                // The value is a placeholder — we swap the DbContext to InMemory below,
                // so no SQL connection is actually opened.
                ["ConnectionStrings:Default"] = "Server=(localdb)\\MSSQLLocalDB;Database=Tests_Ignored;Trusted_Connection=True;",
                ["Jwt:Key"] = "THIS_IS_A_LONG_ENOUGH_TEST_SECRET_KEY_1234567890",
                ["Jwt:Issuer"] = "RetailSuite.Tests",
                ["Jwt:Audience"] = "RetailSuite.Tests",
                ["Payments:Provider"] = "Fake",
                ["Email:Host"] = string.Empty,
                // Non-default super-admin password so the Production secrets validator
                // (if it ever runs in tests) doesn't refuse to start.
                ["SuperAdmin:Password"] = "Tests_Only_NotAdmin@12345",
                ["SuperAdmin:Email"]    = "superadmin@retailsuite.test"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<RetailDbContext>>();
            services.AddDbContext<RetailDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
                options.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            });

            services.AddDataProtection()
                .UseEphemeralDataProtectionProvider();
        });
    }
}
