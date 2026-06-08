using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure.Modules.Locations.Entities;
using RetailSuite.Infrastructure.Modules.Shipping.Entities;
using RetailSuite.Infrastructure.Modules.Tax.Entities;

namespace RetailSuite.Infrastructure.Seeders;

/// <summary>
/// Seeds per-tenant defaults that every new tenant should start with:
///   - A small set of shipping methods so the storefront checkout is functional
///     out of the box (admin can edit / disable later).
///
/// Call from inside the same transaction that creates the tenant so a failure
/// here rolls back the tenant.
/// </summary>
public static class TenantDefaultsSeeder
{
    /// <summary>
    /// Adds the default shipping methods for the given tenant. Idempotent — if
    /// the tenant already has any shipping methods, this is a no-op.
    /// </summary>
    public static async Task SeedAsync(RetailDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        // Idempotency: don't double-seed if rerun for an existing tenant.
        var hasAny = await db.ShippingMethods
            .IgnoreQueryFilters()
            .AnyAsync(s => s.TenantId == tenantId, ct);
        if (hasAny) return;

        var flat = new ShippingMethod(tenantId, "FLAT", "Standard delivery", baseFee: 250m);
        flat.Update(
            name:           null,
            description:    "Delivered to your doorstep",
            baseFee:        null,
            freeOverAmount: 3000m,           // free shipping at Rs 3,000+
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

        // Default empty TaxSettings — tenant admin fills in NTN/STRN under /settings/tax.
        var hasTax = await db.TaxSettings
            .IgnoreQueryFilters()
            .AnyAsync(t => t.TenantId == tenantId, ct);
        if (!hasTax)
        {
            db.TaxSettings.Add(new TaxSettings(tenantId));
        }

        // Default "Main Branch" location — every tenant needs at least one for stock to live in.
        var hasLocation = await db.Locations
            .IgnoreQueryFilters()
            .AnyAsync(l => l.TenantId == tenantId, ct);
        if (!hasLocation)
        {
            var main = new Location(tenantId, code: "MAIN", name: "Main Branch", isDefault: true);
            db.Locations.Add(main);
        }

        await db.SaveChangesAsync(ct);
    }
}
