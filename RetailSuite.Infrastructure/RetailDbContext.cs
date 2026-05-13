using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure.Email;
using RetailSuite.Infrastructure.Modules.Customer.Entities;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Subscriptions.Entities;
using RetailSuite.Infrastructure.Modules.Tenant.Entities;
using RetailSuite.Modules.Accounting.Entities;
using RetailSuite.Modules.Catalog.Entities;
using RetailSuite.Modules.Orders.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure;

public class RetailDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public RetailDbContext(
        DbContextOptions<RetailDbContext> options,
        ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    private Guid? CurrentTenantId => _tenantContext.TenantId;

    // -----------------------------
    // Catalog
    // -----------------------------
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();
    public DbSet<VariantAttributeValue> VariantAttributeValues => Set<VariantAttributeValue>();

    // -----------------------------
    // Inventory
    // -----------------------------
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();

    // -----------------------------
    // Orders
    // -----------------------------
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Customer> Customers => Set<Customer>();
    // -----------------------------
    // Accounting
    // -----------------------------
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<TenantVerificationToken> TenantVerificationTokens => Set<TenantVerificationToken>();

    // -----------------------------
    // Subscriptions
    // -----------------------------
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<SubscriptionInvoice> SubscriptionInvoices => Set<SubscriptionInvoice>();
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();

    // -----------------------------
    // Notifications
    // -----------------------------
    public DbSet<EmailNotification> EmailNotifications => Set<EmailNotification>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("Users");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Email);
            b.Property(x => x.Email).IsRequired();
            b.Property(x => x.PasswordHash).IsRequired();
            b.Property(x => x.IsEmailVerified).HasDefaultValue(false);
        });

        modelBuilder.Entity<User>()
            .HasQueryFilter(u =>
                CurrentTenantId == null || u.TenantId == CurrentTenantId);

        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("Tenants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(200);
            b.Property(x => x.Subdomain).IsRequired().HasMaxLength(100);
            b.HasIndex(x => x.Subdomain).IsUnique();
            b.Property(x => x.BillingEmail).HasMaxLength(250);
            b.Property(x => x.CountryCode).HasMaxLength(2).HasDefaultValue("PK");
        });

        modelBuilder.Entity<TenantVerificationToken>(b =>
        {
            b.ToTable("TenantVerificationTokens");
            b.HasKey(t => t.Id);

            b.Property(t => t.TokenHash).IsRequired().HasMaxLength(128);
            b.Property(t => t.Purpose).IsRequired();
            b.Property(t => t.ExpiresAt).IsRequired();

            b.HasIndex(t => t.TokenHash).IsUnique();
            b.HasIndex(t => new { t.TenantId, t.UserId, t.Purpose });
            b.HasIndex(t => t.ExpiresAt);
        });

        // =====================================================
        // CATALOG CONFIGURATION
        // =====================================================

        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("Products");
            b.HasKey(p => p.Id);

            b.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(200);

            b.Property(p => p.Description)
                .HasMaxLength(2000);

            b.Property(p => p.ImageUrl)
                .HasMaxLength(500);

            b.HasMany(p => p.Variants)
                .WithOne()
                .HasForeignKey(v => v.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductVariant>(b =>
        {
            b.ToTable("ProductVariants");
            b.HasKey(v => v.Id);

            b.Property(v => v.SKU)
                .IsRequired()
                .HasMaxLength(100);

            b.HasIndex(v => new { v.TenantId, v.SKU })
                .IsUnique();

            b.Property(v => v.Price)
                .HasColumnType("decimal(18,2)");

            b.HasMany(v => v.AttributeValues)
                .WithOne(vav => vav.ProductVariant)
                .HasForeignKey(vav => vav.ProductVariantId);

            modelBuilder.Entity<ProductVariant>()
                        .HasOne(v => v.Product)
                        .WithMany(p => p.Variants)
                        .HasForeignKey(v => v.ProductId)
                        .OnDelete(DeleteBehavior.Restrict);

            b.Property(v => v.CostPrice)
                .HasColumnType("decimal(18,2)");

            b.Property(v => v.TaxRate)
                .HasColumnType("decimal(5,4)")
                .HasDefaultValue(0m);
        });

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

            b.HasOne<Category>()
                .WithMany()
                .HasForeignKey(c => c.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductCategory>(b =>
        {
            b.ToTable("ProductCategories");
            b.HasKey(pc => new { pc.ProductId, pc.CategoryId });

            b.HasOne(pc => pc.Product)
                .WithMany()
                .HasForeignKey(pc => pc.ProductId);

            b.HasOne(pc => pc.Category)
                .WithMany()
                .HasForeignKey(pc => pc.CategoryId);
        });

        modelBuilder.Entity<ProductAttribute>(b =>
        {
            b.ToTable("ProductAttributes");
            b.HasKey(a => a.Id);

            b.Property(a => a.Name)
                .IsRequired()
                .HasMaxLength(100);
        });

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

        // =====================================================
        // INVENTORY CONFIGURATION
        // =====================================================

        modelBuilder.Entity<InventoryItem>(b =>
        {
            b.ToTable("InventoryItems");
            b.HasKey(i => i.Id);

            b.HasIndex(i => new { i.TenantId, i.ProductVariantId })
                .IsUnique();

            b.HasMany(i => i.Transactions)
                .WithOne()
                .HasForeignKey(t => t.InventoryItemId)
                .OnDelete(DeleteBehavior.Cascade);

            b.Property(i => i.AverageCost)
                .HasColumnType("decimal(18,4)");

            b.Property(i => i.TotalStockValue)
                .HasColumnType("decimal(18,4)");
        });

        modelBuilder.Entity<InventoryTransaction>(b =>
        {
            b.ToTable("InventoryTransactions");
            b.HasKey(t => t.Id);

            b.Property(t => t.QuantityChange)
                .IsRequired();

            b.Property(t => t.TransactionType)
                .IsRequired();

            b.HasIndex(t => new { t.TenantId, t.ProductVariantId });
            b.HasIndex(t => t.CreatedAt);
        });

        // =====================================================
        // ORDERS CONFIGURATION
        // =====================================================

        modelBuilder.Entity<Customer>(b =>
        {
            b.ToTable("Customers");
            b.HasKey(c => c.Id);

            b.Property(c => c.FirstName)
                .IsRequired()
                .HasMaxLength(150);

            b.Property(c => c.LastName)
                .IsRequired()
                .HasMaxLength(150);

            b.Property(c => c.Email)
                .HasMaxLength(200);

            b.Property(c => c.UserId)
              .IsRequired();

            b.HasIndex(c => new { c.TenantId, c.UserId }).IsUnique();
        });

        modelBuilder.Entity<Order>(b =>
        {
            b.ToTable("Orders");
            b.HasKey(o => o.Id);

            b.Property(o => o.OrderNumber)
                .IsRequired()
                .HasMaxLength(100);

            b.HasIndex(o => new { o.TenantId, o.OrderNumber })
                .IsUnique();

            b.Property(o => o.TotalAmount)
                .HasColumnType("decimal(18,2)");

            b.Property(o => o.TaxAmount)
                .HasColumnType("decimal(18,2)")
                .HasDefaultValue(0m);

            b.HasOne(o => o.Customer)
                    .WithMany()
                    .HasForeignKey(o => o.CustomerId)
                    .OnDelete(DeleteBehavior.Restrict);

            b.HasMany(o => o.Payments)
                  .WithOne()
                  .HasForeignKey(p => p.OrderId)
                  .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(o => o.Items)
                .WithOne()
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(b =>
        {
            b.ToTable("OrderItems");
            b.HasKey(i => i.Id);

            b.Property(i => i.SKU)
                .IsRequired()
                .HasMaxLength(100);

            b.Property(i => i.UnitPrice)
                .HasColumnType("decimal(18,2)");

            b.Property(i => i.TaxRate)
                .HasColumnType("decimal(5,4)")
                .HasDefaultValue(0m);
        });

        // =====================================================
        // ACCOUNTING CONFIGURATION
        // =====================================================

        modelBuilder.Entity<Account>(b =>
        {
            b.ToTable("Accounts");
            b.HasKey(a => a.Id);

            b.Property(a => a.Code).IsRequired().HasMaxLength(50);
            b.Property(a => a.Name).IsRequired().HasMaxLength(200);

            b.HasIndex(a => new { a.TenantId, a.Code }).IsUnique();
        });

        modelBuilder.Entity<JournalEntry>(b =>
        {
            b.ToTable("JournalEntries");
            b.HasKey(j => j.Id);

            b.Property(j => j.Description)
                .IsRequired()
                .HasMaxLength(500);

            b.HasMany(j => j.Lines)
                .WithOne()
                .HasForeignKey(l => l.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JournalEntryLine>(b =>
        {
            b.ToTable("JournalEntryLines");
            b.HasKey(l => l.Id);

            b.Property(l => l.DebitAmount)
                .HasColumnType("decimal(18,2)");

            b.Property(l => l.CreditAmount)
                .HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<Payment>(b =>
        {
            b.ToTable("Payments");
            b.HasKey(p => p.Id);
            b.Property(p => p.Amount)
                .HasColumnType("decimal(18,2)");
            b.Property(p => p.PaymentMethod)
                .IsRequired()
                .HasMaxLength(100);
            b.Property(p => p.TransactionReference)
                .HasMaxLength(200);
        });

        // =====================================================
        // EMAIL NOTIFICATIONS CONFIGURATION
        // =====================================================

        modelBuilder.Entity<EmailNotification>(b =>
        {
            b.ToTable("EmailNotifications");
            b.HasKey(e => e.Id);

            b.Property(e => e.ToAddress).IsRequired().HasMaxLength(250);
            b.Property(e => e.Subject).IsRequired().HasMaxLength(300);
            b.Property(e => e.TemplateKey).IsRequired().HasMaxLength(100);
            b.Property(e => e.Body).IsRequired();
            b.Property(e => e.ErrorMessage).HasMaxLength(1000);
            b.Property(e => e.RelatedEntityType).HasMaxLength(100);
            b.Property(e => e.RelatedEntityId).HasMaxLength(100);

            b.HasIndex(e => new { e.TenantId, e.Status });
            b.HasIndex(e => e.CreatedAt);
        });

        // =====================================================
        // SUBSCRIPTIONS CONFIGURATION
        // =====================================================

        modelBuilder.Entity<SubscriptionPlan>(b =>
        {
            b.ToTable("SubscriptionPlans");
            b.HasKey(p => p.Id);

            b.Property(p => p.Code).IsRequired().HasMaxLength(50);
            b.HasIndex(p => p.Code).IsUnique();

            b.Property(p => p.Name).IsRequired().HasMaxLength(150);
            b.Property(p => p.Description).HasMaxLength(1000);
            b.Property(p => p.Currency).IsRequired().HasMaxLength(3);

            b.Property(p => p.MonthlyPrice).HasColumnType("decimal(18,2)");
            b.Property(p => p.YearlyPrice).HasColumnType("decimal(18,2)");

            b.HasIndex(p => p.IsActive);
        });

        modelBuilder.Entity<TenantSubscription>(b =>
        {
            b.ToTable("TenantSubscriptions");
            b.HasKey(s => s.Id);

            b.Property(s => s.PlanCode).IsRequired().HasMaxLength(50);
            b.Property(s => s.Currency).IsRequired().HasMaxLength(3);
            b.Property(s => s.LastPrice).HasColumnType("decimal(18,2)");

            b.HasOne<SubscriptionPlan>()
                .WithMany()
                .HasForeignKey(s => s.PlanId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(s => new { s.TenantId, s.Status });
            b.HasIndex(s => s.NextBillingAt);
            // One active subscription per tenant — enforce via filtered unique index.
            b.HasIndex(s => s.TenantId)
                .HasFilter("[Status] IN (0, 1, 2, 4)")  // Trialing|Active|PastDue|GracePeriod
                .IsUnique();
        });

        modelBuilder.Entity<SubscriptionInvoice>(b =>
        {
            b.ToTable("SubscriptionInvoices");
            b.HasKey(i => i.Id);

            b.Property(i => i.InvoiceNumber).IsRequired().HasMaxLength(50);
            b.Property(i => i.PlanCode).IsRequired().HasMaxLength(50);
            b.Property(i => i.Reason).HasMaxLength(250);
            b.Property(i => i.Currency).IsRequired().HasMaxLength(3);

            b.Property(i => i.Subtotal).HasColumnType("decimal(18,2)");
            b.Property(i => i.TaxAmount).HasColumnType("decimal(18,2)");
            b.Property(i => i.Total).HasColumnType("decimal(18,2)");
            b.Property(i => i.AmountPaid).HasColumnType("decimal(18,2)");

            b.HasIndex(i => new { i.TenantId, i.InvoiceNumber }).IsUnique();
            b.HasIndex(i => new { i.TenantId, i.Status });
            b.HasIndex(i => i.DueDate);
        });

        modelBuilder.Entity<SubscriptionPayment>(b =>
        {
            b.ToTable("SubscriptionPayments");
            b.HasKey(p => p.Id);

            b.Property(p => p.PaymentMethod).IsRequired().HasMaxLength(50);
            b.Property(p => p.Provider).IsRequired().HasMaxLength(50);
            b.Property(p => p.ProviderTxnRef).HasMaxLength(200);
            b.Property(p => p.Currency).IsRequired().HasMaxLength(3);
            b.Property(p => p.FailureReason).HasMaxLength(500);

            b.Property(p => p.Amount).HasColumnType("decimal(18,2)");

            b.HasOne<SubscriptionInvoice>()
                .WithMany()
                .HasForeignKey(p => p.InvoiceId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(p => new { p.TenantId, p.InvoiceId });
            b.HasIndex(p => p.ProviderTxnRef);
        });

        // =====================================================
        // GLOBAL TENANT FILTER
        // =====================================================

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(TenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(RetailDbContext)
                    .GetMethod(nameof(ApplyTenantFilter),
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.MakeGenericMethod(entityType.ClrType);

                method?.Invoke(this, new object[] { modelBuilder });
            }
        }

        modelBuilder.Ignore<TenantEntity>();

        foreach (var relationship in modelBuilder.Model.GetEntityTypes()
         .SelectMany(e => e.GetForeignKeys()))
        {
            relationship.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }

    private void ApplyTenantFilter<TEntity>(
      ModelBuilder modelBuilder)
      where TEntity : TenantEntity
    {
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e =>
                (CurrentTenantId == null || e.TenantId == CurrentTenantId)
                && !e.IsDeleted);
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
