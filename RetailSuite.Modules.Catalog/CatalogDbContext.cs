using Microsoft.EntityFrameworkCore;
using RetailSuite.Modules.Catalog.Entities;
using RetailSuite.Shared;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace RetailSuite.Modules.Catalog;

public class CatalogDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public CatalogDbContext(
        DbContextOptions<CatalogDbContext> options,
        ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();
    public DbSet<VariantAttributeValue> VariantAttributeValues => Set<VariantAttributeValue>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tenantId = _tenantContext.TenantId;

        // ----------------------------
        // Product
        // ----------------------------
        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("Products");

            b.HasKey(p => p.Id);

            b.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(200);

            b.Property(p => p.Description)
                .HasMaxLength(2000);

            b.Property(p => p.IsActive)
                .IsRequired();

            b.HasMany(p => p.Variants)
                .WithOne()
                .HasForeignKey(v => v.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(p => p.Categories)
                .WithOne(pc => pc.Product)
                .HasForeignKey(pc => pc.ProductId);
        });

        modelBuilder.Entity<Product>()
            .HasQueryFilter(p => tenantId == null || p.TenantId == tenantId);


        // ----------------------------
        // ProductVariant
        // ----------------------------
        modelBuilder.Entity<ProductVariant>(b =>
        {
            b.ToTable("ProductVariants");

            b.HasKey(v => v.Id);

            b.Property(v => v.SKU)
                .IsRequired()
                .HasMaxLength(100);

            b.HasIndex(v => v.SKU);

            b.Property(v => v.Price)
                .HasColumnType("decimal(18,2)");

            b.Property(v => v.IsActive)
                .IsRequired();

            b.HasMany(v => v.AttributeValues)
                .WithOne(vav => vav.ProductVariant)
                .HasForeignKey(vav => vav.ProductVariantId);
        });

        modelBuilder.Entity<ProductVariant>()
            .HasQueryFilter(p => tenantId == null || p.TenantId == tenantId);


        // ----------------------------
        // Category
        // ----------------------------
        modelBuilder.Entity<Category>(b =>
        {
            b.ToTable("Categories");

            b.HasKey(c => c.Id);

            b.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(200);

            b.Property(c => c.Slug)
                .IsRequired()
                .HasMaxLength(200);

            b.HasIndex(c => c.Slug);

            b.HasOne<Category>()
                .WithMany()
                .HasForeignKey(c => c.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Category>()
            .HasQueryFilter(p => tenantId == null || p.TenantId == tenantId);


        // ----------------------------
        // ProductCategory (M:N)
        // ----------------------------
        modelBuilder.Entity<ProductCategory>(b =>
        {
            b.ToTable("ProductCategories");

            b.HasKey(pc => new { pc.ProductId, pc.CategoryId });

            b.HasOne(pc => pc.Product)
                .WithMany(p => p.Categories)
                .HasForeignKey(pc => pc.ProductId);

            b.HasOne(pc => pc.Category)
                .WithMany()
                .HasForeignKey(pc => pc.CategoryId);
        });


        // ----------------------------
        // ProductAttribute
        // ----------------------------
        modelBuilder.Entity<ProductAttribute>(b =>
        {
            b.ToTable("ProductAttributes");

            b.HasKey(a => a.Id);

            b.Property(a => a.Name)
                .IsRequired()
                .HasMaxLength(100);
        });

        modelBuilder.Entity<ProductAttribute>()
            .HasQueryFilter(p => tenantId == null || p.TenantId == tenantId);


        // ----------------------------
        // ProductAttributeValue
        // ----------------------------
        modelBuilder.Entity<ProductAttributeValue>(b =>
        {
            b.ToTable("ProductAttributeValues");

            b.HasKey(av => av.Id);

            b.Property(av => av.Value)
                .IsRequired()
                .HasMaxLength(100);

            b.HasOne<ProductAttribute>()
                .WithMany()
                .HasForeignKey(av => av.AttributeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductAttributeValue>()
            .HasQueryFilter(p => tenantId == null || p.TenantId == tenantId);


        // ----------------------------
        // VariantAttributeValue (M:N)
        // ----------------------------
        modelBuilder.Entity<VariantAttributeValue>(b =>
        {
            b.ToTable("VariantAttributeValues");

            b.HasKey(v => new { v.ProductVariantId, v.ProductAttributeValueId });

            b.HasOne(v => v.ProductVariant)
                .WithMany(pv => pv.AttributeValues)
                .HasForeignKey(v => v.ProductVariantId);

            b.HasOne(v => v.ProductAttributeValue)
                .WithMany()
                .HasForeignKey(v => v.ProductAttributeValueId);
        });

        modelBuilder.Entity<ProductVariant>()
    .HasIndex(v => new { v.TenantId, v.SKU })
    .IsUnique();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<TenantEntity>())
        {
            if (entry.State == EntityState.Added &&
                _tenantContext.TenantId.HasValue)
            {
                entry.Entity.TenantId = _tenantContext.TenantId.Value;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}