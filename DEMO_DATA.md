# RetailSuite Demo Data

Consolidates: `DEMO_DATA_SETUP.md`, `DEMO_DATA_QUICK_START.md`, `DEMO_DATA_SETUP_CHECKLIST.md`, `DEMO_DATA_VISUAL_GUIDE.md`, `DEMO_DATA_INTEGRATION_SUMMARY.md`, `DEMO_USER_IMPLEMENTATION_SUMMARY.md`, `README_DEMO_DATA.md`, `PROJECT_SUMMARY.md`.

## What it is

`DemoDataSeeder` (`RetailSuite.Infrastructure/Seeders/DemoDataSeeder.cs`) runs automatically on API startup and creates a demo tenant with a full product catalog, if it doesn't already exist. Seeding is idempotent — safe to restart the API repeatedly.

## Login credentials (local/demo only — not for production use)

| Account | Email | Password | Role | Tenant |
|---|---|---|---|---|
| Demo Store Admin | `admin@demo-store.com` | `Demo@12345` | Admin | `demo-store` |
| Platform SuperAdmin | `superadmin@retailsuite.com` | `Admin@12345` (default; override via `SuperAdmin:Password` config) | SuperAdmin | — |

## How to seed

```bash
dotnet run --project RetailSuite.Api/RetailSuite.Api.csproj
```

Or via the helper script: `.\seed-demo.ps1`

To reseed: delete the `demo-store` tenant from the database and restart the API.

## Demo catalog

Tenant: **Demo Store** (subdomain `demo-store`), 2 categories, 6 products, 20 variants, 650 total stock units, 17% GST on all items, prices in PKR.

| Product | Category | Sizes | Price (Rs) | Stock | SKU pattern |
|---|---|---|---|---|---|
| Basic T-Shirt | Garments | S/M/L | 499.99–599.99 | 155 | `TSHIRT-SM/MD/LG` |
| Blue Denim Jeans | Garments | S/M/L | 1499.99–1599.99 | 130 | `JEANS-SM/MD/LG` |
| Formal Shirt | Garments | S/M/L | 899.99–999.99 | 95 | `SHIRT-SM/MD/LG` |
| Running Shoes | Shoes | 6–9 | 2499.99 | 100 | `RUNSHOES-6/7/8/9` |
| Casual Sneakers | Shoes | 6–9 | 1799.99 | 150 | `SNEAKERS-6/7/8/9` |
| Formal Dress Shoes | Shoes | 7–9 | 3499.99 | 60 | `FORMAL-7/8/9` |

Barcodes are sequential EAN-style, e.g. `8901234001001` (T-Shirt Small) through `8901234006003` (Formal Shoes size 9). Cost prices sit ~40–50% below retail for margin testing.

## Testing in POS

1. Log in to StoreAdmin with the `demo-store` tenant.
2. Go to Point of Sale.
3. Search by SKU (`TSHIRT-SM`), name (`T-Shirt`), or barcode (`8901234001001`).
4. Add to cart, check stock levels, complete checkout.
