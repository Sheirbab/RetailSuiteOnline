# 🎯 Complete Demo Store Setup - Start Here!

## ✨ You Now Have Everything!

Your RetailSuite demo store is **fully configured** with:
- ✅ Demo tenant (demo-store)
- ✅ Admin user account
- ✅ 6 products with 20 variants
- ✅ 650 units of inventory
- ✅ Realistic pricing & tax
- ✅ Ready-to-use credentials

---

## 🔐 Login Now

### Admin Account
```
URL: https://localhost:7060/

Email:    admin@demo-store.com
Password: Demo@12345
Tenant:   demo-store
```

### SuperAdmin Account
```
Email:    superadmin@retailsuite.com
Password: Admin@12345
```

---

## 🚀 Getting Started in 3 Steps

### Step 1: Start the API
```bash
cd D:\Shehriyar\Project\RetailSuite_Starter
dotnet run --project RetailSuite.Api/RetailSuite.Api.csproj
```

### Step 2: Start the StoreAdmin UI
```bash
cd D:\Shehriyar\Project\RetailSuite_Starter
dotnet run --project RetailSuite.StoreAdmin/RetailSuite.StoreAdmin.csproj --launch-profile https
```

StoreAdmin reads the API base URL from `RetailSuite.StoreAdmin/appsettings*.json`:

```
Api:BaseUrl = https://localhost:59777/
```

### Step 3: Watch the API Console
Look for:
```
✓ Created demo tenant: Demo Store (demo-store)
✓ Created admin user: admin@demo-store.com (password: Demo@12345)
✓ Created 6 products with 20 total variants
```

### Step 4: Login & Test
1. Open: https://localhost:7060/
2. Use credentials above
3. Go to **Point of Sale**
4. Search for products (try: "TSHIRT-SM", "RUNSHOES-7", or scan "8901234001001")
5. Add to cart and checkout!

---

## 📦 What's in Your Demo Store

### Products (6 total)
| Category | Product | Price | Stock |
|----------|---------|-------|-------|
| Garments | T-Shirt | ₨499-599 | 155 |
| Garments | Jeans | ₨1,499-1,599 | 130 |
| Garments | Formal Shirt | ₨899-999 | 95 |
| Shoes | Running Shoes | ₨2,499 | 100 |
| Shoes | Sneakers | ₨1,799 | 150 |
| Shoes | Formal Shoes | ₨3,499 | 60 |

**Total: 20 variants, 650 units**

### Sample Barcodes to Scan
```
8901234001001 → T-Shirt Small
8901234002001 → Jeans Small
8901234004002 → Running Shoes Size 7
8901234005003 → Sneakers Size 8
8901234006002 → Formal Shoes Size 8
```

---

## 🧪 Test Scenarios

### Test 1: Product Search
1. Go to POS
2. Search "TSHIRT" → See all T-shirt sizes
3. Search "RUNSHOES" → See all running shoes
4. Search by full name: "Jeans"

### Test 2: Barcode Scanning
1. Use any barcode from list above
2. Paste in search box
3. Product should appear
4. Add to cart

### Test 3: Checkout Flow
1. Add multiple products to cart
2. Set quantities
3. Verify prices + tax
4. Complete checkout
5. Verify order created
6. Check inventory updated

### Test 4: Inventory Tracking
1. Add 10 T-Shirt Small to cart
2. Complete checkout
3. Stock should decrease from 50 to 40
4. Add more T-shirts
5. Verify stock continues to decrease

---

## 📊 Demo Store Details

### Tenant Info
- **Name**: Demo Store
- **Subdomain**: demo-store
- **Status**: Active

### Categories
1. **Garments** - Clothing items
2. **Shoes** - Footwear

### Products with Variants
- T-Shirt: S, M, L (3 variants)
- Jeans: S, M, L (3 variants)
- Formal Shirt: S, M, L (3 variants)
- Running Shoes: 6, 7, 8, 9 (4 variants)
- Casual Sneakers: 6, 7, 8, 9 (4 variants)
- Formal Shoes: 7, 8, 9 (3 variants)

### Pricing
- Average Price: ₨1,599
- Price Range: ₨499 - ₨3,499
- Margin: ~55% average
- Tax: 17% GST on all items

---

## 🎓 Features to Test

✅ **Multi-tenant isolation** - Data isolated to demo-store  
✅ **Product search** - By SKU, name, barcode  
✅ **Variant selection** - Multiple sizes per product  
✅ **Inventory tracking** - Real-time stock updates  
✅ **Tax calculation** - 17% GST on totals  
✅ **Pricing** - Realistic costs & margins  
✅ **User authentication** - Secure login  
✅ **Role-based access** - Admin role demo  
✅ **Barcode scanning** - Ready-to-test barcodes  
✅ **Checkout flow** - Complete order processing  

---

## 🔐 Security

### Your Data
- ✅ Passwords are securely hashed (BCrypt)
- ✅ Users isolated by tenant
- ✅ JWT token authentication
- ✅ HTTPS/TLS encrypted

### Default Credentials
- ⚠️ For DEMO purposes only
- ⚠️ Change in production
- ⚠️ Use strong unique passwords

---

## 📝 Key Information

### Admin User
- **Email**: admin@demo-store.com
- **Password**: Demo@12345
- **Role**: Admin (full store access)
- **Tenant**: demo-store

### SuperAdmin User
- **Email**: superadmin@retailsuite.com
- **Password**: Admin@12345
- **Role**: SuperAdmin (platform-wide)
- **Access**: All tenants

---

## 📚 Documentation

| Doc | Purpose |
|-----|---------|
| **DEMO_USER_CREDENTIALS.md** | User account details |
| **README_DEMO_DATA.md** | Quick reference |
| **DEMO_DATA_QUICK_START.md** | Step-by-step guide |
| **DEMO_DATA_SETUP.md** | Product catalog |
| **DEMO_DATA_VISUAL_GUIDE.md** | Visual structures |
| **DEMO_USER_IMPLEMENTATION_SUMMARY.md** | What was added |

---

## ❓ FAQ

**Q: How do I login?**  
A: Go to https://localhost:7060/, use admin@demo-store.com / Demo@12345

**Q: Can I test checkout?**  
A: Yes! All products are ready. Add to cart and complete checkout.

**Q: Can I scan barcodes?**  
A: Yes! Try any barcode from the list or paste in search.

**Q: What if I mess up inventory?**  
A: Restart the API - demo data will reseed (idempotent).

**Q: Can I change the password?**  
A: Yes, in your account settings (if implemented).

**Q: Is this production-ready?**  
A: No, this is demo data for testing only.

**Q: What tenant do I use?**  
A: demo-store

---

## ⚡ Quick Commands

### Start API
```bash
dotnet run --project RetailSuite.Api/RetailSuite.Api.csproj
```

### Login URL
```
https://localhost:7060/
```

### Admin Credentials
```
admin@demo-store.com
Demo@12345
```

---

## 🎯 Next Steps

1. ✅ Start the API
2. ✅ Verify seeding output
3. ✅ Login to StoreAdmin
4. ✅ Navigate to POS
5. ✅ Test product search
6. ✅ Test barcode scanning
7. ✅ Add products to cart
8. ✅ Complete checkout
9. ✅ Verify order created
10. ✅ Check inventory updated

---

## 📞 Need Help?

- **Login issues?** See `DEMO_USER_CREDENTIALS.md`
- **Product details?** See `DEMO_DATA_SETUP.md`
- **Technical details?** See `DEMO_DATA_INTEGRATION_SUMMARY.md`
- **All docs?** Check `DOCUMENTATION_INDEX.md`

---

## ✅ Status

- ✅ Demo tenant created
- ✅ Admin user created
- ✅ 6 products added
- ✅ 20 variants configured
- ✅ 650 units in inventory
- ✅ Barcodes assigned
- ✅ Tax configured
- ✅ Pricing set
- ✅ Build successful
- ✅ **Ready to test!**

---

## 🎉 You're All Set!

Your RetailSuite demo store is **fully configured and ready to use**.

**Start the API now and login!**

```bash
dotnet run --project RetailSuite.Api/RetailSuite.Api.csproj
```

Then go to: https://localhost:7060/

Login with:
- **Email**: admin@demo-store.com
- **Password**: Demo@12345

**Enjoy testing your e-commerce platform!** 🛍️
