
using Microsoft.EntityFrameworkCore;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Tenant;

public class TenantDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public TenantDbContext(
        DbContextOptions<TenantDbContext> options,
        ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }


    public DbSet<Entities.Tenant> Tenants => Set<Entities.Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Entities.Tenant>(b =>
        {
            b.ToTable("Tenants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(200);
            b.Property(x => x.Subdomain).IsRequired().HasMaxLength(100);
            b.HasIndex(x => x.Subdomain).IsUnique();
        });

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
