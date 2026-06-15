# RetailSuite Demo Data Setup

## Overview
The RetailSuite system now includes automatic demo data seeding. When the API starts, it will automatically create a demo tenant with sample products, categories, and inventory data if it doesn't already exist.

## Demo Tenant Details

### Tenant Information
- **Name**: Demo Store
- **Subdomain**: `demo-store`
- **Status**: Active

## Product Categories

### 1. Garments
Contains clothing items with size variations.

### 2. Shoes
Contains footwear with size variations.

## Demo Products

### Garments Category

#### 1. Basic T-Shirt
- **Description**: Comfortable everyday cotton t-shirt
- **Variants**: 3 sizes (Small, Medium, Large)
- **SKU Examples**: TSHIRT-SM, TSHIRT-MD, TSHIRT-LG
- **Price Range**: Rs 499.99 - Rs 599.99
- **Cost Price Range**: Rs 200 - Rs 240
- **Barcodes**: 8901234001001 - 8901234001003
- **Tax Rate**: 17% GST
- **Stock**: 50, 45, 60 units

#### 2. Blue Denim Jeans
- **Description**: Classic blue denim jeans for all occasions
- **Variants**: 3 sizes (Small, Medium, Large)
- **SKU Examples**: JEANS-SM, JEANS-MD, JEANS-LG
- **Price Range**: Rs 1499.99 - Rs 1599.99
- **Cost Price Range**: Rs 600 - Rs 650
- **Barcodes**: 8901234002001 - 8901234002003
- **Tax Rate**: 17% GST
- **Stock**: 35, 40, 55 units

#### 3. Formal Shirt
- **Description**: Professional formal shirt for business wear
- **Variants**: 3 sizes (Small, Medium, Large)
- **SKU Examples**: SHIRT-SM, SHIRT-MD, SHIRT-LG
- **Price Range**: Rs 899.99 - Rs 999.99
- **Cost Price Range**: Rs 400 - Rs 450
- **Barcodes**: 8901234003001 - 8901234003003
- **Tax Rate**: 17% GST
- **Stock**: 25, 30, 35 units

### Shoes Category

#### 4. Professional Running Shoes
- **Description**: Lightweight athletic running shoes with cushioned sole
- **Variants**: 4 sizes (6, 7, 8, 9)
- **SKU Examples**: RUNSHOES-6, RUNSHOES-7, RUNSHOES-8, RUNSHOES-9
- **Price**: Rs 2499.99 (all sizes)
- **Cost Price**: Rs 1100
- **Barcodes**: 8901234004001 - 8901234004004
- **Tax Rate**: 17% GST
- **Stock**: 20, 25, 30, 25 units

#### 5. Casual Sneakers
- **Description**: Trendy everyday casual sneakers for comfort and style
- **Variants**: 4 sizes (6, 7, 8, 9)
- **SKU Examples**: SNEAKERS-6, SNEAKERS-7, SNEAKERS-8, SNEAKERS-9
- **Price**: Rs 1799.99 (all sizes)
- **Cost Price**: Rs 800
- **Barcodes**: 8901234005001 - 8901234005004
- **Tax Rate**: 17% GST
- **Stock**: 40, 35, 30, 45 units

#### 6. Formal Dress Shoes
- **Description**: Premium leather formal shoes for business and formal occasions
- **Variants**: 3 sizes (7, 8, 9)
- **SKU Examples**: FORMAL-7, FORMAL-8, FORMAL-9
- **Price**: Rs 3499.99 (all sizes)
- **Cost Price**: Rs 1500
- **Barcodes**: 8901234006001 - 8901234006003
- **Tax Rate**: 17% GST
- **Stock**: 15, 20, 25 units

## Quick Stats

| Metric | Count |
|--------|-------|
| **Tenant** | 1 |
| **Categories** | 2 |
| **Products** | 6 |
| **Product Variants** | 20 |
| **Inventory Items** | 20 |
| **Total Stock Units** | 650 |

## How to Seed Demo Data

### Option 1: Automatic (Recommended)
Simply start the API:
```bash
dotnet run --project RetailSuite.Api/RetailSuite.Api.csproj
```

The demo data will be seeded automatically if it doesn't already exist.

### Option 2: Using the PowerShell Script
```bash
.\seed-demo.ps1
```

### Option 3: Manual Seeding
If you need to reseed or seed programmatically, you can call:
```csharp
var context = GetDbContext(); // Your DbContext instance
await DemoDataSeeder.SeedDemoDataAsync(context);
```

## Testing in POS

Once the demo data is seeded, you can test it in the POS system:

1. **Log in** to the StoreAdmin with the Demo Store tenant
2. **Navigate** to Point of Sale (POS)
3. **Search** products by:
   - **SKU**: e.g., "TSHIRT-SM", "RUNSHOES-7"
   - **Product Name**: e.g., "T-Shirt", "Jeans"
   - **Barcode**: Scan any of the EAN barcodes (8901234001001, etc.)
4. **Add to Cart** by clicking on any variant
5. **View Stock** levels in the POS interface

## Testing with Barcodes

You can test barcode scanning with these sample barcodes:
- T-Shirt Small: `8901234001001`
- T-Shirt Medium: `8901234001002`
- Jeans Small: `8901234002001`
- Running Shoes Size 7: `8901234004002`
- Casual Sneakers Size 8: `8901234005003`
- Formal Shoes Size 8: `8901234006002`

## Data Structure

The demo data follows these relationships:

```
Tenant (Demo Store)
├── Categories
│   ├── Garments
│   │   ├── Products
│   │   │   ├── Basic T-Shirt
│   │   │   ├── Blue Denim Jeans
│   │   │   └── Formal Shirt
│   │   └── Variants & Inventory
│   └── Shoes
│       ├── Products
│       │   ├── Running Shoes
│       │   ├── Casual Sneakers
│       │   └── Formal Dress Shoes
│       └── Variants & Inventory
```

## Idempotency

The seeding is **idempotent** - if you run it multiple times, it will only create the demo data once. Subsequent runs will detect the existing demo tenant and skip seeding.

## Resetting Demo Data

If you need to reset and reseed the demo data:

1. **Delete** the demo store data from the database (or reset the entire database)
2. **Restart** the API - it will automatically reseed the demo data

## Notes

- All products have a **17% GST tax rate** (Indian standard)
- All prices are in **Pakistani Rupees (Rs)**
- Stock quantities are realistic for a retail environment
- Cost prices are set approximately 40-50% below retail prices for profit margin testing
- Barcodes follow a standard EAN pattern for scanning tests
- Products are organized by realistic categories for e-commerce testing
