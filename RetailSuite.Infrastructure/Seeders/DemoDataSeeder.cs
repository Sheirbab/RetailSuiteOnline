using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure.Modules.Customer.Model;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Tenant.Entities;
using RetailSuite.Modules.Catalog.Entities;

namespace RetailSuite.Infrastructure.Seeders;

/// <summary>
/// Seeds demo data for testing: demo tenant, categories, products with variations, and inventory
/// </summary>
public static class DemoDataSeeder
{
    public static async Task SeedDemoDataAsync(RetailDbContext context)
    {
        // Check if demo tenant already exists
        var demoTenant = await context.Tenants.FirstOrDefaultAsync(t => t.Subdomain == "demo-store");
        if (demoTenant != null)
        {
            Console.WriteLine("Demo data already seeded.");
            return;
        }

        Console.WriteLine("Seeding demo data...");

        // Create demo tenant
        demoTenant = new Tenant("Demo Store", "demo-store");
        context.Tenants.Add(demoTenant);
        await context.SaveChangesAsync();

        Console.WriteLine($"✓ Created demo tenant: {demoTenant.Name} ({demoTenant.Subdomain})");

        // Create Admin user for demo tenant
        var adminEmail = "admin@demo-store.com";
        var adminPassword = "Demo@12345";
        var adminPasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword);
        var adminUser = new User(demoTenant.Id, adminEmail, adminPasswordHash, UserRole.Admin);
        context.Users.Add(adminUser);
        await context.SaveChangesAsync();

        Console.WriteLine($"✓ Created admin user: {adminEmail} (password: {adminPassword})");

        // Create categories
        var garmentsCategory = new Category("Garments", "garments", null) { TenantId = demoTenant.Id };
        var shoesCategory = new Category("Shoes", "shoes", null) { TenantId = demoTenant.Id };

        context.Categories.AddRange(garmentsCategory, shoesCategory);
        await context.SaveChangesAsync();

        Console.WriteLine("✓ Created categories: Garments, Shoes");

        // GARMENTS PRODUCTS
        // T-Shirt
        var tshirt = new Product("Basic T-Shirt", "Comfortable everyday cotton t-shirt") { TenantId = demoTenant.Id };
        context.Products.Add(tshirt);
        await context.SaveChangesAsync();

        var tshirtSmall = new ProductVariant(tshirt.Id, "TSHIRT-SM", 499.99m, 200m) { TenantId = demoTenant.Id };
        tshirtSmall.SetBarcode("8901234001001");
        tshirtSmall.SetTaxRate(0.17m); // 17% GST

        var tshirtMedium = new ProductVariant(tshirt.Id, "TSHIRT-MD", 549.99m, 220m) { TenantId = demoTenant.Id };
        tshirtMedium.SetBarcode("8901234001002");
        tshirtMedium.SetTaxRate(0.17m);

        var tshirtLarge = new ProductVariant(tshirt.Id, "TSHIRT-LG", 599.99m, 240m) { TenantId = demoTenant.Id };
        tshirtLarge.SetBarcode("8901234001003");
        tshirtLarge.SetTaxRate(0.17m);

        tshirt.AddVariant(tshirtSmall);
        tshirt.AddVariant(tshirtMedium);
        tshirt.AddVariant(tshirtLarge);

        // Jeans
        var jeans = new Product("Blue Denim Jeans", "Classic blue denim jeans for all occasions") { TenantId = demoTenant.Id };
        context.Products.Add(jeans);
        await context.SaveChangesAsync();

        var jeansSmall = new ProductVariant(jeans.Id, "JEANS-SM", 1499.99m, 600m) { TenantId = demoTenant.Id };
        jeansSmall.SetBarcode("8901234002001");
        jeansSmall.SetTaxRate(0.17m);

        var jeansMedium = new ProductVariant(jeans.Id, "JEANS-MD", 1499.99m, 600m) { TenantId = demoTenant.Id };
        jeansMedium.SetBarcode("8901234002002");
        jeansMedium.SetTaxRate(0.17m);

        var jeansLarge = new ProductVariant(jeans.Id, "JEANS-LG", 1599.99m, 650m) { TenantId = demoTenant.Id };
        jeansLarge.SetBarcode("8901234002003");
        jeansLarge.SetTaxRate(0.17m);

        jeans.AddVariant(jeansSmall);
        jeans.AddVariant(jeansMedium);
        jeans.AddVariant(jeansLarge);

        // Shirt
        var shirt = new Product("Formal Shirt", "Professional formal shirt for business wear") { TenantId = demoTenant.Id };
        context.Products.Add(shirt);
        await context.SaveChangesAsync();

        var shirtSmall = new ProductVariant(shirt.Id, "SHIRT-SM", 899.99m, 400m) { TenantId = demoTenant.Id };
        shirtSmall.SetBarcode("8901234003001");
        shirtSmall.SetTaxRate(0.17m);

        var shirtMedium = new ProductVariant(shirt.Id, "SHIRT-MD", 949.99m, 420m) { TenantId = demoTenant.Id };
        shirtMedium.SetBarcode("8901234003002");
        shirtMedium.SetTaxRate(0.17m);

        var shirtLarge = new ProductVariant(shirt.Id, "SHIRT-LG", 999.99m, 450m) { TenantId = demoTenant.Id };
        shirtLarge.SetBarcode("8901234003003");
        shirtLarge.SetTaxRate(0.17m);

        shirt.AddVariant(shirtSmall);
        shirt.AddVariant(shirtMedium);
        shirt.AddVariant(shirtLarge);

        // SHOES PRODUCTS
        // Running Shoes
        var runningShoes = new Product("Professional Running Shoes", "Lightweight athletic running shoes with cushioned sole") { TenantId = demoTenant.Id };
        context.Products.Add(runningShoes);
        await context.SaveChangesAsync();

        var runshoesSize6 = new ProductVariant(runningShoes.Id, "RUNSHOES-6", 2499.99m, 1100m) { TenantId = demoTenant.Id };
        runshoesSize6.SetBarcode("8901234004001");
        runshoesSize6.SetTaxRate(0.17m);

        var runshoesSize7 = new ProductVariant(runningShoes.Id, "RUNSHOES-7", 2499.99m, 1100m) { TenantId = demoTenant.Id };
        runshoesSize7.SetBarcode("8901234004002");
        runshoesSize7.SetTaxRate(0.17m);

        var runshoesSize8 = new ProductVariant(runningShoes.Id, "RUNSHOES-8", 2499.99m, 1100m) { TenantId = demoTenant.Id };
        runshoesSize8.SetBarcode("8901234004003");
        runshoesSize8.SetTaxRate(0.17m);

        var runshoesSize9 = new ProductVariant(runningShoes.Id, "RUNSHOES-9", 2499.99m, 1100m) { TenantId = demoTenant.Id };
        runshoesSize9.SetBarcode("8901234004004");
        runshoesSize9.SetTaxRate(0.17m);

        runningShoes.AddVariant(runshoesSize6);
        runningShoes.AddVariant(runshoesSize7);
        runningShoes.AddVariant(runshoesSize8);
        runningShoes.AddVariant(runshoesSize9);

        // Casual Sneakers
        var sneakers = new Product("Casual Sneakers", "Trendy everyday casual sneakers for comfort and style") { TenantId = demoTenant.Id };
        context.Products.Add(sneakers);
        await context.SaveChangesAsync();

        var sneakersSize6 = new ProductVariant(sneakers.Id, "SNEAKERS-6", 1799.99m, 800m) { TenantId = demoTenant.Id };
        sneakersSize6.SetBarcode("8901234005001");
        sneakersSize6.SetTaxRate(0.17m);

        var sneakersSize7 = new ProductVariant(sneakers.Id, "SNEAKERS-7", 1799.99m, 800m) { TenantId = demoTenant.Id };
        sneakersSize7.SetBarcode("8901234005002");
        sneakersSize7.SetTaxRate(0.17m);

        var sneakersSize8 = new ProductVariant(sneakers.Id, "SNEAKERS-8", 1799.99m, 800m) { TenantId = demoTenant.Id };
        sneakersSize8.SetBarcode("8901234005003");
        sneakersSize8.SetTaxRate(0.17m);

        var sneakersSize9 = new ProductVariant(sneakers.Id, "SNEAKERS-9", 1799.99m, 800m) { TenantId = demoTenant.Id };
        sneakersSize9.SetBarcode("8901234005004");
        sneakersSize9.SetTaxRate(0.17m);

        sneakers.AddVariant(sneakersSize6);
        sneakers.AddVariant(sneakersSize7);
        sneakers.AddVariant(sneakersSize8);
        sneakers.AddVariant(sneakersSize9);

        // Formal Shoes
        var formalShoes = new Product("Formal Dress Shoes", "Premium leather formal shoes for business and formal occasions") { TenantId = demoTenant.Id };
        context.Products.Add(formalShoes);
        await context.SaveChangesAsync();

        var formalSize7 = new ProductVariant(formalShoes.Id, "FORMAL-7", 3499.99m, 1500m) { TenantId = demoTenant.Id };
        formalSize7.SetBarcode("8901234006001");
        formalSize7.SetTaxRate(0.17m);

        var formalSize8 = new ProductVariant(formalShoes.Id, "FORMAL-8", 3499.99m, 1500m) { TenantId = demoTenant.Id };
        formalSize8.SetBarcode("8901234006002");
        formalSize8.SetTaxRate(0.17m);

        var formalSize9 = new ProductVariant(formalShoes.Id, "FORMAL-9", 3499.99m, 1500m) { TenantId = demoTenant.Id };
        formalSize9.SetBarcode("8901234006003");
        formalSize9.SetTaxRate(0.17m);

        formalShoes.AddVariant(formalSize7);
        formalShoes.AddVariant(formalSize8);
        formalShoes.AddVariant(formalSize9);

        context.SaveChanges();

        // Associate products with categories using correct constructor
        var garmentsPC = new ProductCategory(tshirt.Id, garmentsCategory.Id);
        var garmentsPCJeans = new ProductCategory(jeans.Id, garmentsCategory.Id);
        var garmentsPCShirt = new ProductCategory(shirt.Id, garmentsCategory.Id);

        var shoesPC1 = new ProductCategory(runningShoes.Id, shoesCategory.Id);
        var shoesPC2 = new ProductCategory(sneakers.Id, shoesCategory.Id);
        var shoesPC3 = new ProductCategory(formalShoes.Id, shoesCategory.Id);

        context.ProductCategories.AddRange(garmentsPC, garmentsPCJeans, garmentsPCShirt, shoesPC1, shoesPC2, shoesPC3);
        await context.SaveChangesAsync();

        Console.WriteLine("✓ Created 6 products with 20 total variants");

        // Create inventory for all variants
        var allVariants = new[]
        {
            tshirtSmall, tshirtMedium, tshirtLarge,
            jeansSmall, jeansMedium, jeansLarge,
            shirtSmall, shirtMedium, shirtLarge,
            runshoesSize6, runshoesSize7, runshoesSize8, runshoesSize9,
            sneakersSize6, sneakersSize7, sneakersSize8, sneakersSize9,
            formalSize7, formalSize8, formalSize9
        };

        var inventoryItems = new List<InventoryItem>();
        var stockQuantities = new[] { 50, 45, 60, 35, 40, 55, 25, 30, 35, 20, 25, 30, 25, 40, 35, 30, 45, 15, 20, 25 };

        for (int i = 0; i < allVariants.Length; i++)
        {
            var inventoryItem = new InventoryItem(allVariants[i].Id, lowStockThreshold: 10) { TenantId = demoTenant.Id };
            inventoryItem.ReceiveStock(stockQuantities[i], allVariants[i].CostPrice);
            inventoryItems.Add(inventoryItem);

            // Sync stock quantity to ProductVariant for POS display
            allVariants[i].StockQuantity = stockQuantities[i];
            allVariants[i].AverageCost = allVariants[i].CostPrice;
        }

        context.InventoryItems.AddRange(inventoryItems);
        await context.SaveChangesAsync();

        Console.WriteLine("✓ Created inventory for all variants with stock quantities");
        Console.WriteLine("\n=== Demo Data Summary ===");
        Console.WriteLine($"Tenant: {demoTenant.Name} ({demoTenant.Subdomain})");
        Console.WriteLine($"Admin User: {adminEmail} / {adminPassword}");
        Console.WriteLine($"Categories: 2 (Garments, Shoes)");
        Console.WriteLine($"Products: 6 (3 Garments, 3 Shoes)");
        Console.WriteLine($"Product Variants: 20");
        Console.WriteLine($"Inventory Items: 20");
        Console.WriteLine("===========================");
    }
}
