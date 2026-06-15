# RetailSuite Demo Data - Quick Start Guide

## 🎯 What Was Created

I've set up comprehensive demo data seeding for your RetailSuite project. Here's what you now have:

### ✅ Automated Seeding
- Demo data automatically seeds when the API starts
- Fully idempotent (won't duplicate data on multiple runs)
- Integrated into `Program.cs` in RetailSuite.Api

### ✅ Demo Tenant & Products
- **1 Demo Tenant**: "Demo Store" (subdomain: `demo-store`)
- **2 Categories**: Garments, Shoes
- **6 Products**: T-Shirt, Jeans, Formal Shirt, Running Shoes, Sneakers, Formal Shoes
- **20 Product Variants**: Sizes/colors for each product
- **20 Inventory Items**: Full stock tracking with cost prices

### ✅ Sample Data Details
- ✓ Real prices in PKR (Pakistani Rupees)
- ✓ 17% GST tax rates applied
- ✓ Realistic stock quantities (650 total units)
- ✓ Barcode codes for scanner testing (8901234001001, etc.)
- ✓ Cost prices for margin calculations
- ✓ Organized by product categories

## 🚀 How to Use

### Start the API (Auto-Seeds Demo Data)
```bash
cd D:\Shehriyar\Project\RetailSuite_Starter
dotnet run --project RetailSuite.Api/RetailSuite.Api.csproj
```

When the API starts, you'll see console output:
```
✓ Created demo tenant: Demo Store (demo-store)
✓ Created admin user: admin@demo-store.com (password: Demo@12345)
✓ Created categories: Garments, Shoes
✓ Created 6 products with 20 total variants
✓ Created inventory for all variants with stock quantities

=== Demo Data Summary ===
Tenant: Demo Store (demo-store)
Admin User: admin@demo-store.com / Demo@12345
Categories: 2 (Garments, Shoes)
Products: 6 (3 Garments, 3 Shoes)
Product Variants: 20
Inventory Items: 20
===========================
```

### Login to StoreAdmin
1. Go to https://localhost:7096/
2. Use credentials:
   - **Email**: admin@demo-store.com
   - **Password**: Demo@12345
   - **Tenant**: demo-store
3. Navigate to Point of Sale (POS)
4. Search products:
   - By SKU: "TSHIRT-SM", "RUNSHOES-7", etc.
   - By Product Name: "T-Shirt", "Jeans", etc.
   - By Barcode: Scan any barcode
5. Add items to cart and test checkout

## 📦 Files Added/Modified

### Created Files:
```
RetailSuite.Infrastructure/
├── Seeders/
│   └── DemoDataSeeder.cs          ← Demo data seeding logic
├── Program.cs                      ← Updated to call seeder
└── ProductVariant.cs              ← Added SetBarcode() method
```

### Documentation:
```
DEMO_DATA_SETUP.md                ← Detailed product catalog
seed-demo.ps1                      ← Optional PowerShell helper script
```

## 🛍️ Product Catalog

### Garments (3 products, 9 variants)
| Product | SKU | Price | Stock |
|---------|-----|-------|-------|
| Basic T-Shirt | TSHIRT-SM/MD/LG | ₨499-599 | 155 units |
| Blue Denim Jeans | JEANS-SM/MD/LG | ₨1499-1599 | 130 units |
| Formal Shirt | SHIRT-SM/MD/LG | ₨899-999 | 95 units |

### Shoes (3 products, 11 variants)
| Product | SKU | Price | Stock |
|---------|-----|-------|-------|
| Running Shoes | RUNSHOES-6/7/8/9 | ₨2499 | 100 units |
| Casual Sneakers | SNEAKERS-6/7/8/9 | ₨1799 | 150 units |
| Formal Shoes | FORMAL-7/8/9 | ₨3499 | 60 units |

## 🔐 Login Credentials

### Demo Store Admin
```
Email: admin@demo-store.com
Password: Demo@12345
Tenant: demo-store
Role: Admin
```

### Platform SuperAdmin
```
Email: superadmin@retailsuite.com
Password: Admin@12345
Role: SuperAdmin
```

## 🧪 Testing Features

### Test Barcode Scanning
Use these sample barcodes in your POS scanner:
- `8901234001001` - T-Shirt Small
- `8901234004002` - Running Shoes Size 7
- `8901234005003` - Sneakers Size 8
- And 17 more in the product catalog!

### Test Multi-Variant Search
- Search "TSHIRT" to see all T-shirt sizes
- Search "RUNSHOES" to see all running shoe sizes
- Search by partial product names

### Test Inventory Tracking
- Monitor stock levels in POS
- Track cost prices and margins
- Test low stock warnings

## 💡 Key Features

✅ **Idempotent**: Safe to run multiple times without duplication
✅ **Realistic Data**: Proper pricing, tax rates, and stock levels
✅ **Multi-Tenant Ready**: Proper tenant isolation in demo data
✅ **Comprehensive**: Full product hierarchy with categories and variants
✅ **Easy Testing**: Ready-to-use barcodes and SKUs for POS testing

## 📝 Next Steps

1. ✅ Start the API and verify seeding works
2. ✅ Test POS with demo products
3. ✅ Verify barcode scanning functionality
4. ✅ Test checkout and order creation
5. ✅ Validate inventory tracking

## ❓ Troubleshooting

**Demo data not showing?**
- Check that the API started successfully
- Look for the "=== Demo Data Summary ===" message in console
- Verify the Demo Store tenant exists in database

**Products not appearing in POS?**
- Ensure you're logged into the "Demo Store" tenant
- Refresh the POS page
- Check browser console for errors

**Need to reseed?**
- Delete the "demo-store" tenant from the database
- Restart the API and it will automatically reseed

---

Happy testing! 🎉 Your RetailSuite demo data is ready to use.
