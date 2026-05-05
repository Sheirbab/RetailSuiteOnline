# RetailSuite Demo Data - Visual Guide

## 🎬 Demo Store Structure

```
┌─────────────────────────────────────────────────┐
│          DEMO STORE (demo-store)                │
│         Pakistani E-Commerce Setup              │
└─────────────────────────────────────────────────┘
                          │
            ┌─────────────┴─────────────┐
            │                           │
       ┌────▼────┐               ┌─────▼────┐
       │ GARMENTS │               │  SHOES   │
       └────┬────┘               └─────┬────┘
            │                          │
    ┌───────┼───────┐         ┌───────┼───────┐
    │       │       │         │       │       │
  ┌─▼─┐  ┌─▼─┐  ┌──▼──┐    ┌─▼────┐ ┌──▼───┐ ┌──▼────┐
  │ T │  │   │  │Form │    │Running│ │Casual│ │Formal │
  │Sh │  │Jea│  │Shirt│    │Shoes │ │Sneak │ │Shoes  │
  │irt│  │ns │  │     │    │      │ │ers   │ │       │
  └─┬─┘  └─┬─┘  └──┬──┘    └─┬────┘ └──┬───┘ └──┬────┘
    │      │       │         │         │       │
  ┌─┴──┐ ┌─┴──┐ ┌──┴──┐    ┌─┴───┐ ┌──┴───┐ ┌──┴───┐
  │ S  │ │ S  │ │  S  │    │  6  │ │  6   │ │  7   │
  │ M  │ │ M  │ │  M  │    │  7  │ │  7   │ │  8   │
  │ L  │ │ L  │ │  L  │    │  8  │ │  8   │ │  9   │
  │    │ │    │ │     │    │  9  │ │  9   │ │      │
  └────┘ └────┘ └─────┘    └─────┘ └──────┘ └──────┘
   155    130     95         100     150      60
  units  units   units      units   units   units
```

---

## 📦 Product Inventory Overview

### Garments (₨894 - ₨1599 price range)

```
┌─────────────────────────────────────────┐
│  BASIC T-SHIRT                          │
│  ₨499.99 - ₨599.99 per unit            │
├─────────────────────────────────────────┤
│  Size   │  SKU        │ Stock │ Barcode │
├─────────┼─────────────┼───────┼─────────┤
│  Small  │ TSHIRT-SM   │  50   │ ...001  │
│  Medium │ TSHIRT-MD   │  45   │ ...002  │
│  Large  │ TSHIRT-LG   │  60   │ ...003  │
│                              TOTAL: 155 │
└─────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│  BLUE DENIM JEANS                       │
│  ₨1,499.99 - ₨1,599.99 per unit        │
├─────────────────────────────────────────┤
│  Size   │  SKU        │ Stock │ Barcode │
├─────────┼─────────────┼───────┼─────────┤
│  Small  │ JEANS-SM    │  35   │ ...001  │
│  Medium │ JEANS-MD    │  40   │ ...002  │
│  Large  │ JEANS-LG    │  55   │ ...003  │
│                              TOTAL: 130 │
└─────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│  FORMAL SHIRT                           │
│  ₨899.99 - ₨999.99 per unit            │
├─────────────────────────────────────────┤
│  Size   │  SKU        │ Stock │ Barcode │
├─────────┼─────────────┼───────┼─────────┤
│  Small  │ SHIRT-SM    │  25   │ ...001  │
│  Medium │ SHIRT-MD    │  30   │ ...002  │
│  Large  │ SHIRT-LG    │  35   │ ...003  │
│                               TOTAL: 95 │
└─────────────────────────────────────────┘

GARMENTS TOTAL: 380 units
```

### Shoes (₨1,799 - ₨3,499 price range)

```
┌──────────────────────────────────────────┐
│  PROFESSIONAL RUNNING SHOES              │
│  ₨2,499.99 per unit (all sizes)         │
├──────────────────────────────────────────┤
│  Size   │  SKU         │ Stock │ Barcode │
├─────────┼──────────────┼───────┼─────────┤
│  6      │ RUNSHOES-6   │  20   │ ...001  │
│  7      │ RUNSHOES-7   │  25   │ ...002  │
│  8      │ RUNSHOES-8   │  30   │ ...003  │
│  9      │ RUNSHOES-9   │  25   │ ...004  │
│                               TOTAL: 100 │
└──────────────────────────────────────────┘

┌──────────────────────────────────────────┐
│  CASUAL SNEAKERS                         │
│  ₨1,799.99 per unit (all sizes)         │
├──────────────────────────────────────────┤
│  Size   │  SKU         │ Stock │ Barcode │
├─────────┼──────────────┼───────┼─────────┤
│  6      │ SNEAKERS-6   │  40   │ ...001  │
│  7      │ SNEAKERS-7   │  35   │ ...002  │
│  8      │ SNEAKERS-8   │  30   │ ...003  │
│  9      │ SNEAKERS-9   │  45   │ ...004  │
│                               TOTAL: 150 │
└──────────────────────────────────────────┘

┌──────────────────────────────────────────┐
│  FORMAL DRESS SHOES                      │
│  ₨3,499.99 per unit (all sizes)         │
├──────────────────────────────────────────┤
│  Size   │  SKU        │ Stock │ Barcode  │
├─────────┼─────────────┼───────┼──────────┤
│  7      │ FORMAL-7    │  15   │ ...001   │
│  8      │ FORMAL-8    │  20   │ ...002   │
│  9      │ FORMAL-9    │  25   │ ...003   │
│                               TOTAL: 60  │
└──────────────────────────────────────────┘

SHOES TOTAL: 310 units
```

---

## 💰 Pricing & Profit Analysis

```
┌─────────────────────────────────────────────────────────┐
│           MARGIN ANALYSIS                               │
├─────────────────────────────────────────────────────────┤
│  GARMENTS                                               │
│  T-Shirt:    Cost ₨200-240   →  Sell ₨499-599  (50%+) │
│  Jeans:      Cost ₨600       →  Sell ₨1499-1599 (60%) │
│  Shirt:      Cost ₨400-450   →  Sell ₨899-999  (54%)  │
│                                                         │
│  SHOES                                                  │
│  Running:    Cost ₨1100      →  Sell ₨2499     (55%)  │
│  Sneakers:   Cost ₨800       →  Sell ₨1799     (55%)  │
│  Formal:     Cost ₨1500      →  Sell ₨3499     (57%)  │
│                                                         │
│  Average Profit Margin: ~55%                           │
│  Tax Applied: 17% GST on all items                     │
└─────────────────────────────────────────────────────────┘
```

---

## 🧪 Testing Workflow

```
API Start
   │
   ├─► SuperAdminSeeder
   │   └─► Creates superadmin user
   │
   └─► DemoDataSeeder
       ├─► Check if demo-store exists?
       │   ├─ YES → Skip (already seeded)
       │   └─ NO  → Proceed with seeding
       │
       ├─► Create Tenant (Demo Store)
       ├─► Create Categories (Garments, Shoes)
       ├─► Create Products (6)
       ├─► Create Variants (20)
       ├─► Create Category Mappings (6)
       ├─► Create Inventory Items (20)
       │
       └─► Print Summary to Console

Database Updated ✓

Login to StoreAdmin
   │
   ├─► Select "Demo Store" tenant
   ├─► Navigate to Point of Sale
   │
   └─► Test Features:
       ├─► Search by SKU
       ├─► Search by Product Name
       ├─► Scan Barcodes
       ├─► Add to Cart
       ├─► View Stock Levels
       └─► Complete Checkout
```

---

## 📊 Statistics

```
╔════════════════════════════════════════════════╗
║        DEMO STORE STATISTICS                   ║
╠════════════════════════════════════════════════╣
║ Tenant:           1                            ║
║ Categories:       2                            ║
║ Products:         6                            ║
║ Product Variants: 20                           ║
║ Total SKUs:       20                           ║
║ Inventory Items:  20                           ║
║ Total Stock:      650 units                    ║
║ Total Categories: 6 product-category mappings ║
║                                                ║
║ SKUs with Barcodes: 20/20 (100%)              ║
║ Products with Tax: 20/20 (100%)               ║
║ Variants with Cost: 20/20 (100%)              ║
║                                                ║
║ Price Range: ₨499 - ₨3,499                   ║
║ Average Price: ₨1,599                         ║
║ Tax Rate: 17% GST (all items)                 ║
║ Average Margin: ~55%                          ║
╚════════════════════════════════════════════════╝
```

---

## 🎯 POS Testing Checklist

```
□ API starts and seeds demo data
□ Console shows "Demo Data Summary"
□ Can login to "demo-store" tenant
□ POS page loads without errors
□ Can search products by name
□ Can search products by SKU
□ Can search products by barcode
□ Product list shows correct stock
□ Can add T-Shirt Small (TSHIRT-SM) to cart
□ Can add Running Shoes Size 7 (RUNSHOES-7) to cart
□ Can scan barcode 8901234001001
□ Prices show with 17% tax included
□ Stock decreases after adding to cart
□ Can complete checkout
□ Order is created successfully
□ Inventory is updated after checkout
```

---

## 🚀 Ready to Use!

Your RetailSuite platform now has complete demo data for testing all features:

- ✅ Product catalog with 6 products
- ✅ 20 variants with different options
- ✅ 650 units of inventory
- ✅ Realistic pricing and margins
- ✅ Tax calculations (17% GST)
- ✅ Barcode support for scanning
- ✅ Full POS testing capability

**Start your API and begin testing!** 🎉
