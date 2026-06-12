using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure.Modules.Customer.Entities;
using RetailSuite.Infrastructure.Modules.Locations.Entities;
using RetailSuite.Infrastructure.Modules.Shipping.Entities;
using RetailSuite.Infrastructure.Modules.Tax.Entities;
using RetailSuite.Modules.Accounting.Entities;

namespace RetailSuite.Infrastructure.Seeders;

/// <summary>
/// Seeds per-tenant defaults that every new tenant should start with:
///   - Chart of Accounts (Cash, Inventory, AR, Tax Payable, Revenue, COGS)
///   - Default shipping methods (FLAT + PICKUP)
///   - Empty TaxSettings row (admin fills in NTN/STRN)
///   - A default "Main Branch" location (per-location inventory needs at least one)
///
/// Every block is independently idempotent — if the tenant already has the rows
/// for one block but not another, the missing block still gets seeded. Safe to
/// re-run on existing tenants to backfill anything that was missed.
///
/// Call from inside the same transaction that creates the tenant so a failure
/// here rolls back the tenant.
/// </summary>
public static class TenantDefaultsSeeder
{
    /// <summary>
    /// Stable marker used to find the tenant's walk-in customer row. We match by
    /// (FirstName, LastName) inside the tenant scope.
    /// </summary>
    public const string WalkInFirstName = "Walk-in";
    public const string WalkInLastName  = "Customer";

    public static async Task SeedAsync(RetailDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        await SeedAccountsAsync(db, tenantId, ct);
        await SeedShippingMethodsAsync(db, tenantId, ct);
        await SeedTaxSettingsAsync(db, tenantId, ct);
        await SeedDefaultLocationAsync(db, tenantId, ct);
        await SeedWalkInCustomerAsync(db, tenantId, ct);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Get the tenant's walk-in customer id — used by sale flows whenever the
    /// cashier hasn't attached a real customer. Self-heals: if the walk-in row
    /// is missing, the full seeder runs first.
    /// </summary>
    public static async Task<Guid> GetWalkInCustomerIdAsync(
        RetailDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        var existing = await db.Customers
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId
                     && c.FirstName == WalkInFirstName
                     && c.LastName == WalkInLastName)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        if (existing.HasValue) return existing.Value;

        await SeedAsync(db, tenantId, ct);

        return await db.Customers
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId
                     && c.FirstName == WalkInFirstName
                     && c.LastName == WalkInLastName)
            .Select(c => c.Id)
            .FirstAsync(ct);
    }

    // ----- Chart of Accounts -------------------------------------------------

    private static async Task SeedAccountsAsync(RetailDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existingCodes = await db.Accounts
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId)
            .Select(a => a.Code)
            .ToListAsync(ct);

        // Only add the codes that are missing — supports backfilling partially
        // seeded tenants (e.g., legacy tenants from before this seeder existed).
        var defaults = new (string Code, string Name, AccountType Type)[]
        {
            ("1000", "Cash",                AccountType.Asset),
            ("1100", "Inventory",           AccountType.Asset),
            ("1200", "Accounts Receivable", AccountType.Asset),
            ("2000", "Tax Payable",         AccountType.Liability),
            ("4000", "Revenue",             AccountType.Revenue),
            ("5000", "Cost of Goods Sold",  AccountType.Expense),
        };

        foreach (var (code, name, type) in defaults)
        {
            if (existingCodes.Contains(code)) continue;
            db.Accounts.Add(new Account(code, name, type) { TenantId = tenantId });
        }
    }

    // ----- Shipping methods --------------------------------------------------

    private static async Task SeedShippingMethodsAsync(RetailDbContext db, Guid tenantId, CancellationToken ct)
    {
        var hasAny = await db.ShippingMethods
            .IgnoreQueryFilters()
            .AnyAsync(s => s.TenantId == tenantId, ct);
        if (hasAny) return;

        var flat = new ShippingMethod(tenantId, "FLAT", "Standard delivery", baseFee: 250m);
        flat.Update(
            name:           null,
            description:    "Delivered to your doorstep",
            baseFee:        null,
            freeOverAmount: 3000m,                // free shipping at Rs 3,000+
            isActive:       true,
            sortOrder:      1,
            eta:            "2–4 working days");

        var pickup = new ShippingMethod(tenantId, "PICKUP", "Pick up at store", baseFee: 0m, isPickup: true);
        pickup.Update(
            name:           null,
            description:    "Collect from the store — no delivery fee",
            baseFee:        null,
            freeOverAmount: null,
            isActive:       true,
            sortOrder:      2,
            eta:            "Same day");

        db.ShippingMethods.Add(flat);
        db.ShippingMethods.Add(pickup);
    }

    // ----- Tax settings ------------------------------------------------------

    private static async Task SeedTaxSettingsAsync(RetailDbContext db, Guid tenantId, CancellationToken ct)
    {
        var hasTax = await db.TaxSettings
            .IgnoreQueryFilters()
            .AnyAsync(t => t.TenantId == tenantId, ct);
        if (!hasTax)
            db.TaxSettings.Add(new TaxSettings(tenantId));
    }

    // ----- Default location --------------------------------------------------

    private static async Task SeedDefaultLocationAsync(RetailDbContext db, Guid tenantId, CancellationToken ct)
    {
        var hasLocation = await db.Locations
            .IgnoreQueryFilters()
            .AnyAsync(l => l.TenantId == tenantId, ct);
        if (!hasLocation)
            db.Locations.Add(new Location(tenantId, code: "MAIN", name: "Main Branch", isDefault: true));
    }

    // ----- Walk-in customer --------------------------------------------------

    /// <summary>
    /// Every tenant gets a single "Walk-in Customer" row used as the FK target
    /// when a sale doesn't have a real customer attached. Phone/email left null —
    /// keeps lookups by phone (DoCustomerLookup) from matching this row by mistake.
    /// </summary>
    private static async Task SeedWalkInCustomerAsync(RetailDbContext db, Guid tenantId, CancellationToken ct)
    {
        var exists = await db.Customers
            .IgnoreQueryFilters()
            .AnyAsync(c => c.TenantId == tenantId
                        && c.FirstName == WalkInFirstName
                        && c.LastName == WalkInLastName, ct);
        if (exists) return;

        // UserId = Guid.Empty — no user account is attached to walk-ins.
        var walkIn = new Customer(
            userId:    Guid.Empty,
            firstName: WalkInFirstName,
            lastName:  WalkInLastName,
            email:     null,
            phone:     null);

        // Force TenantId so this row lives under the right tenant even if
        // the DbContext's tenant-stamping hook misses it (it shouldn't,
        // but explicit is safer).
        walkIn.TenantId = tenantId;

        db.Customers.Add(walkIn);
    }
}
