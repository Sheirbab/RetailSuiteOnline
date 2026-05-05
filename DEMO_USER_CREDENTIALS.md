# Demo Tenant User Account

## Login Credentials

### Demo Store Admin
- **Tenant**: demo-store
- **Email**: admin@demo-store.com
- **Password**: Demo@12345
- **Role**: Admin

### SuperAdmin (Platform)
- **Email**: superadmin@retailsuite.com
- **Password**: Admin@12345
- **Role**: SuperAdmin
- **Note**: Can manage all tenants

---

## How to Login

### 1. Visit the StoreAdmin Application
```
URL: https://localhost:7096/
```

### 2. Enter Credentials
- **Email**: admin@demo-store.com
- **Password**: Demo@12345
- **Tenant**: demo-store

### 3. You're In!
Once logged in, you can:
- Access the Point of Sale (POS)
- Manage products and inventory
- Create and process orders
- View reports and analytics

---

## Account Details

### Admin User
- Created automatically when demo data is seeded
- Has Admin role for the Demo Store tenant
- Can manage products, inventory, and orders
- Full access to StoreAdmin features

### Account Isolation
- The admin user is isolated to the demo-store tenant
- Cannot access data from other tenants
- Password is securely hashed with BCrypt

---

## First Time Setup

1. **Start the API**
   ```bash
   dotnet run --project RetailSuite.Api/RetailSuite.Api.csproj
   ```

2. **Watch for Seeding Output**
   ```
   ✓ Created demo tenant: Demo Store (demo-store)
   ✓ Created admin user: admin@demo-store.com (password: Demo@12345)
   ```

3. **Login to StoreAdmin**
   - Go to https://localhost:7096/
   - Use credentials above
   - Select demo-store tenant

4. **Start Testing**
   - Navigate to Point of Sale
   - Search and add products
   - Test checkout flow

---

## Password Change

To change the admin password:

1. Login to StoreAdmin with current credentials
2. Go to Account Settings (if available)
3. Change password
4. New password will be securely hashed

---

## User Roles

### Admin
- ✅ Full access to tenant features
- ✅ Manage products and inventory
- ✅ Process orders
- ✅ View reports
- ✅ Manage staff (if applicable)

### Staff
- ✅ Limited access
- ✅ Can process orders
- ✅ Can view inventory
- ❌ Cannot edit products
- ❌ Cannot view reports

### Customer
- ✅ Browse products
- ✅ Place orders
- ✅ View order history
- ✅ Manage account

### SuperAdmin
- ✅ Access all tenants
- ✅ Manage tenants
- ✅ Global reports
- ✅ System administration

---

## Demo Data Seeding

When the API starts, it automatically:
1. Creates the demo-store tenant
2. Creates an admin user for the tenant
3. Creates product categories
4. Creates 6 products with 20 variants
5. Sets up inventory for all variants
6. Prints a summary to console

All seeding is **idempotent** - running multiple times won't create duplicates.

---

## Security Notes

### Password Hashing
- All passwords are hashed using BCrypt with salt
- Never stored in plain text
- Cannot be reversed
- Each password has unique salt

### Multi-Tenant Isolation
- Users are isolated by tenant
- Admin user for demo-store cannot access other tenants
- Data is automatically filtered by tenant

### API Authentication
- JWT tokens used for API requests
- Tokens expire after a set time
- Secure transmission over HTTPS

---

## Troubleshooting

### Can't Login?
- Check email address: `admin@demo-store.com`
- Check password: `Demo@12345`
- Make sure you selected demo-store tenant
- Clear browser cache and try again

### Account Locked?
- Accounts don't auto-lock in demo
- Check the error message for details
- Try logging in again

### Forgot Password?
- For demo purposes, restart the API
- Demo data will reseed with default credentials
- In production, use password reset functionality

---

## Related Documentation

- `DEMO_DATA_SETUP.md` - Complete demo store information
- `DEMO_DATA_QUICK_START.md` - Quick start guide
- `README_DEMO_DATA.md` - Quick reference

---

**Note**: These are default demo credentials. Change passwords in production environments!
