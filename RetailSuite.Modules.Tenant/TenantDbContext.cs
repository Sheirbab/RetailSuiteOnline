
using Microsoft.EntityFrameworkCore;
using RetailSuite.Modules.Tenant.Entities;

namespace RetailSuite.Modules.Tenant;

public class TenantDbContext : DbContext
{
    public TenantDbContext(DbContextOptions<TenantDbContext> options)
        : base(options) { }

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
}
