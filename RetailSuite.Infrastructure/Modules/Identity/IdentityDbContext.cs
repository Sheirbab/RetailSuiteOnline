using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Identity;

public class IdentityDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public IdentityDbContext(
        DbContextOptions<IdentityDbContext> options,
        ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("Users");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Email);
            b.Property(x => x.Email).IsRequired();
            b.Property(x => x.PasswordHash).IsRequired();
        });
        var tenantId = _tenantContext.TenantId;

        modelBuilder.Entity<User>()
            .HasQueryFilter(u =>
                tenantId == null || u.TenantId == tenantId);

    }
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<TenantEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (_tenantContext.TenantId.HasValue)
                {
                    entry.Entity.TenantId = _tenantContext.TenantId.Value;
                }
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}