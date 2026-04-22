using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Customer.Model;
using Serilog;

namespace RetailSuite.Api.Seeding;

/// <summary>
/// Idempotent seeder — creates the platform-level SuperAdmin user if it does not
/// already exist.
///
/// SuperAdmin is stored with TenantId = Guid.Empty so the global tenant query
/// filter is bypassed at query time (TenantContext returns null for Guid.Empty,
/// which makes the HasQueryFilter a no-op).
///
/// The insert uses raw SQL to avoid EF's SaveChangesAsync auto-assigning
/// TenantId from the request-scoped ITenantContext (which is null at startup).
/// </summary>
public static class SuperAdminSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db     = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var email    = config["SuperAdmin:Email"]    ?? "superadmin@retailsuite.com";
        var password = config["SuperAdmin:Password"] ?? "Admin@12345";

        // Search across all tenants — IgnoreQueryFilters bypasses tenant filter
        var exists = await db.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Role == UserRole.SuperAdmin);

        if (exists)
        {
            Log.Information("SuperAdmin already exists — skipping seed.");
            return;
        }

        var id           = Guid.NewGuid();
        var tenantId     = Guid.Empty;          // sentinel: "no tenant"
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);
        var role         = (int)UserRole.SuperAdmin;
        var createdAt    = DateTime.UtcNow;

        // Raw SQL bypasses the EF interceptor that overwrites TenantId from
        // the request-scoped ITenantContext (which is null at startup).
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO Users (Id, TenantId, Email, PasswordHash, Role, IsDeleted, CreatedAt)
            VALUES ({id}, {tenantId}, {email}, {passwordHash}, {role}, 0, {createdAt})");

        Log.Information("SuperAdmin seeded — email: {Email}", email);
    }
}
