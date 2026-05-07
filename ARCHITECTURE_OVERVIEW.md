# 🏗️ RetailSuite Architecture Overview

**Framework**: Clean Architecture + Multi-Tenancy  
**Technology**: .NET 8, Blazor Server, EF Core, SQL Server  
**Status**: ✅ Fully Implemented

---

## 🎨 System Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                        CLIENT LAYER                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────┐  │
│  │  Blazor Server   │  │  Browser/HTTP    │  │  Web Client  │  │
│  │  (StoreAdmin)    │  │  Client          │  │  (Storefront)│  │
│  │                  │  │                  │  │              │  │
│  │ - Dashboard      │  │ - JavaScript     │  │ - Shop       │  │
│  │ - POS            │  │ - Interactivity  │  │ - Cart       │  │
│  │ - Admin Panel    │  │ - Barcode Input  │  │ - Checkout   │  │
│  │ - Inventory      │  │                  │  │              │  │
│  │ - Orders         │  │                  │  │              │  │
│  └──────────────────┘  └──────────────────┘  └──────────────┘  │
│           │                      │                    │          │
└───────────┼──────────────────────┼────────────────────┼──────────┘
            │                      │                    │
            └──────────────────────┴────────────────────┘
                          ▼
        ┌─────────────────────────────────────┐
        │       NETWORK (HTTPS)               │
        └─────────────────────────────────────┘
                          ▼
┌─────────────────────────────────────────────────────────────────┐
│                      API LAYER                                   │
│              (RetailSuite.Api - ASP.NET Core)                    │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │              MIDDLEWARE PIPELINE                        │   │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐              │   │
│  │  │ Logging  │→ │ Exception│→ │ Auth     │              │   │
│  │  │ Middleware│ │Middleware│ │ Middleware│              │   │
│  │  └──────────┘  └──────────┘  └──────────┘              │   │
│  └─────────────────────────────────────────────────────────┘   │
│                          ▼                                       │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │                  CONTROLLERS (11)                       │   │
│  ├─────────────────────────────────────────────────────────┤   │
│  │                                                          │   │
│  │ ┌──────────────────────────────────────────────────┐   │   │
│  │ │ Auth         Product      Orders      Inventory │   │   │
│  │ │ Customers    Categories   Payments    Reports   │   │   │
│  │ │ Tenants      Attributes   Accounting  Sales     │   │   │
│  │ └──────────────────────────────────────────────────┘   │   │
│  │                                                          │   │
│  │ Total: 51 Endpoints                                     │   │
│  └─────────────────────────────────────────────────────────┘   │
│                          ▼                                       │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │        REQUEST/RESPONSE HANDLING                        │   │
│  │  ┌──────────────┐       ┌──────────────┐              │   │
│  │  │ DTOs         │◄─────►│ Models       │              │   │
│  │  │ (Request)    │       │ (Response)   │              │   │
│  │  └──────────────┘       └──────────────┘              │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
                          ▼
┌─────────────────────────────────────────────────────────────────┐
│              BUSINESS LOGIC LAYER                                │
│        (RetailSuite.Infrastructure - Services)                   │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │              MODULE SERVICES                            │   │
│  ├─────────────────────────────────────────────────────────┤   │
│  │                                                          │   │
│  │ ┌──────────────────┐  ┌──────────────────┐             │   │
│  │ │ Order Service    │  │ Inventory Service│             │   │
│  │ │ - Create Order   │  │ - Receive Stock  │             │   │
│  │ │ - Confirm Order  │  │ - Issue Stock    │             │   │
│  │ │ - Cancel Order   │  │ - Adjust Stock   │             │   │
│  │ │ - Return Order   │  │ - Calculate COGS │             │   │
│  │ └──────────────────┘  └──────────────────┘             │   │
│  │                                                          │   │
│  │ ┌──────────────────┐  ┌──────────────────┐             │   │
│  │ │ Customer Service │  │ Accounting       │             │   │
│  │ │ - Register       │  │ Service          │             │   │
│  │ │ - Update Profile │  │ - Journal Entry  │             │   │
│  │ │ - Payment History│  │ - GL Reporting   │             │   │
│  │ └──────────────────┘  └──────────────────┘             │   │
│  │                                                          │   │
│  │ ┌──────────────────┐  ┌──────────────────┐             │   │
│  │ │ Payment Service  │  │ Sale Service     │             │   │
│  │ │ - Process Payment│  │ - POS Sale       │             │   │
│  │ │ - Record Payment │  │ - Generate Order │             │   │
│  │ └──────────────────┘  └──────────────────┘             │   │
│  │                                                          │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │              CROSS-CUTTING SERVICES                     │   │
│  ├─────────────────────────────────────────────────────────┤   │
│  │  Email Service │ Payment Gateway │ Error Handling │      │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
                          ▼
┌─────────────────────────────────────────────────────────────────┐
│              DOMAIN MODEL LAYER                                  │
│     (RetailSuite.Infrastructure - Entities + DTOs)              │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  CATALOG MODULE                  │  ORDERING MODULE              │
│  ┌──────────────────┐            │  ┌──────────────────┐        │
│  │ Product          │            │  │ Order            │        │
│  │ ProductVariant   │            │  │ OrderItem        │        │
│  │ ProductAttribute │            │  │ OrderStatus      │        │
│  │ ProductCategory  │            │  └──────────────────┘        │
│  │ Category         │            │                              │
│  └──────────────────┘            │  INVENTORY MODULE             │
│                                   │  ┌──────────────────┐        │
│  TENANT MODULE                    │  │ InventoryItem    │        │
│  ┌──────────────────┐            │  │ InventoryTx      │        │
│  │ Tenant           │            │  │ InventoryTxType  │        │
│  └──────────────────┘            │  └──────────────────┘        │
│                                   │                              │
│  IDENTITY MODULE                  │  ACCOUNTING MODULE           │
│  ┌──────────────────┐            │  ┌──────────────────┐        │
│  │ User             │            │  │ Account          │        │
│  │ UserRole         │            │  │ JournalEntry     │        │
│  │ Customer         │            │  │ JournalEntryLine │        │
│  └──────────────────┘            │  │ Payment          │        │
│                                   │  └──────────────────┘        │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
                          ▼
┌─────────────────────────────────────────────────────────────────┐
│              DATA ACCESS LAYER                                   │
│        (RetailSuite.Infrastructure - EF Core DbContext)          │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │          RetailDbContext (EF Core)                       │  │
│  │                                                           │  │
│  │  DbSet<Product>             DbSet<Order>                │  │
│  │  DbSet<ProductVariant>      DbSet<OrderItem>            │  │
│  │  DbSet<InventoryItem>       DbSet<Customer>             │  │
│  │  DbSet<Tenant>              DbSet<User>                 │  │
│  │  DbSet<Account>             DbSet<JournalEntry>         │  │
│  │                                                           │  │
│  │  ┌─────────────────────────────────────────────────┐   │  │
│  │  │ MULTI-TENANCY ENFORCEMENT                      │   │  │
│  │  │ • Query Filters by TenantId                    │   │  │
│  │  │ • Automatic Tenant Context Injection           │   │  │
│  │  │ • Prevents Cross-Tenant Data Access            │   │  │
│  │  └─────────────────────────────────────────────────┘   │  │
│  │                                                           │  │
│  │  ┌─────────────────────────────────────────────────┐   │  │
│  │  │ MIGRATIONS (3 Applied)                          │   │  │
│  │  │ • 20250407: Initial Schema                      │   │  │
│  │  │ • 20250416: Cascade Delete Fixes                │   │  │
│  │  │ • 20250422: Phase 2 Features                    │   │  │
│  │  └─────────────────────────────────────────────────┘   │  │
│  │                                                           │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
                          ▼
┌─────────────────────────────────────────────────────────────────┐
│              PERSISTENCE LAYER                                   │
│                (SQL Server LocalDB)                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │                DATABASE TABLES                          │   │
│  ├─────────────────────────────────────────────────────────┤   │
│  │                                                          │   │
│  │  Catalogs         Orders          Identity              │   │
│  │  ├─ Products      ├─ Orders       ├─ Users              │   │
│  │  ├─ Variants      ├─ OrderItems   ├─ Customers          │   │
│  │  ├─ Categories    └─ OrderStatus  └─ UserRoles          │   │
│  │  ├─ Attributes                                          │   │
│  │  └─ Variants_Attrs    Inventory      Accounting         │   │
│  │                       ├─ InventoryItems   ├─ Accounts   │   │
│  │  Tenants              ├─ InventoryTx      ├─ JournalTx  │   │
│  │  └─ Tenants           └─ InventoryTxTypes ├─ JETx_Lines │   │
│  │                                           └─ Payments   │   │
│  │                                                          │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                   │
│  ┌──────────────────────────────────────┐                       │
│  │ Storage Size: ~10-50 MB (Demo Data)  │                       │
│  │ Connection: LocalDB (Development)    │                       │
│  │ Backups: Manual (Development)        │                       │
│  └──────────────────────────────────────┘                       │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘

```

---

## 🔐 Multi-Tenancy Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                  REQUEST ROUTING                            │
└─────────────────────────────────────────────────────────────┘
        demo-store.local  →  tenant_id = demo-store
        admin.local       →  tenant_id = admin

                    ▼

┌─────────────────────────────────────────────────────────────┐
│            CURRENT TENANT CONTEXT (Middleware)              │
│  - Extract tenant from subdomain or JWT claim              │
│  - Resolve to tenant record in database                     │
│  - Store in CurrentUserContext for this request             │
└─────────────────────────────────────────────────────────────┘
                    ▼

┌─────────────────────────────────────────────────────────────┐
│         QUERY FILTERS (Applied Automatically)               │
│  Tenant A User (tenant_id = A):                             │
│    SELECT * FROM Products WHERE TenantId = A  ✓             │
│    SELECT * FROM Orders WHERE TenantId = A    ✓             │
│  Tenant B User (tenant_id = B):                             │
│    SELECT * FROM Products WHERE TenantId = B  ✓             │
│    (Cannot see Products from Tenant A)        ✗             │
└─────────────────────────────────────────────────────────────┘
                    ▼

┌─────────────────────────────────────────────────────────────┐
│           DATA ISOLATION GUARANTEE                          │
│  Each tenant sees ONLY their own data:                      │
│  - Products & Inventory                                     │
│  - Orders & Customers                                       │
│  - Staff & Permissions                                      │
│  - Accounting & Payments                                    │
└─────────────────────────────────────────────────────────────┘

```

---

## 🔄 Request Flow Diagram

### Example: Create Order (POS Checkout)

```
CLIENT (Blazor POS)
    │
    ├─→ User scans product barcode
    │   (JavaScript: focus input, capture barcode text)
    │
    ├─→ User adds to cart
    │   (Blazor: UpdateCart method)
    │
    ├─→ User clicks Checkout
    │   (Blazor: SubmitCheckout method)
    │
    └─→ HTTP POST /api/orders/pos-sale
        │ {
        │   items: [ { variantId, quantity, price } ],
        │   paymentMethod: "Cash"
        │ }
        │
        ▼
API (RetailSuite.Api)
    │
    ├─→ [AuthMiddleware]
    │   Validate JWT token
    │   Extract UserId, TenantId, Role
    │
    ├─→ OrdersController.CreatePosSale()
    │   Check authorization (Admin or Staff)
    │   Extract tenant from context
    │
    ├─→ OrderService.CreateOrder()
    │   │
    │   ├─ Validate order items
    │   ├─ Check stock availability
    │   ├─ Calculate totals & tax
    │   ├─ Create Order entity
    │   ├─ Create OrderItems
    │   └─ Save to database
    │
    ├─→ InventoryService.IssueStock()
    │   │
    │   ├─ For each item:
    │   │   ├─ Decrement InventoryItem.CurrentStock
    │   │   ├─ Calculate COGS
    │   │   ├─ Create InventoryTransaction
    │   │   └─ Update ProductVariant.StockQuantity
    │   │
    │   └─ Save changes to database
    │
    ├─→ PaymentService.RegisterPayment()
    │   │
    │   ├─ Create Payment record
    │   ├─ Create accounting journal entry
    │   └─ Update Order.PaidAmount
    │
    ├─→ [ExceptionMiddleware]
    │   If any error: Return error response with details
    │
    └─→ Return OrderDto (201 Created)
        {
          orderId: "guid",
          orderNumber: "ORD-001",
          totalAmount: 5000,
          paidAmount: 5000,
          status: "Completed",
          items: [ ... ]
        }
        │
        ▼
CLIENT (Blazor POS)
    │
    ├─→ Display receipt
    │   - Order number
    │   - Items list
    │   - Total & payment
    │   - Date/Time
    │
    ├─→ Show "Success" message
    │
    └─→ Clear cart & await next transaction

```

---

## 📊 Data Model Relationships

```
┌─────────────────────────────────────────────────────────────┐
│                    PRODUCT HIERARCHY                        │
└─────────────────────────────────────────────────────────────┘

    Tenant (1)
        ▲
        │ owns 1 to many
        │
    ├─→ Category (multiple)
    │   ├─→ Product (multiple)
    │   │   ├─→ ProductVariant (multiple)
    │   │   │   ├─→ VariantAttributeValue
    │   │   │   ├─→ InventoryItem (1)
    │   │   │   │   ├─→ InventoryTransaction (multiple)
    │   │   │   │   └─→ CurrentStock, AverageCost
    │   │   │   └─→ Barcode, SKU, Price, CostPrice
    │   │   │
    │   │   └─→ ProductAttribute (shared)
    │   │
    │   └─→ ProductCategory (junction)


┌─────────────────────────────────────────────────────────────┐
│                    ORDER HIERARCHY                          │
└─────────────────────────────────────────────────────────────┘

    Tenant (1)
        ▲
        │ owns 1 to many
        │
    ├─→ Customer (multiple)
    │   ├─→ Order (multiple)
    │   │   ├─→ OrderItem (multiple)
    │   │   │   ├─→ ProductVariant (reference)
    │   │   │   ├─→ UnitPrice, Quantity, Total
    │   │   │   └─→ TaxAmount, TaxRate
    │   │   │
    │   │   ├─→ Payment (multiple)
    │   │   │   ├─→ Amount, Method, Date
    │   │   │   └─→ Reference (receipt #, check #)
    │   │   │
    │   │   └─→ OrderStatus
    │   │       ├─ Draft
    │   │       ├─ Confirmed
    │   │       ├─ Completed
    │   │       └─ Cancelled
    │   │
    │   └─→ User (reference)
    │       ├─ Email, Password, Role
    │       └─ Permissions


┌─────────────────────────────────────────────────────────────┐
│                 ACCOUNTING HIERARCHY                        │
└─────────────────────────────────────────────────────────────┘

    Tenant (1)
        ▲
        │ owns 1 to many
        │
    └─→ Account (multiple)
        ├─→ 1000 - Inventory (Asset)
        ├─→ 2000 - Accounts Payable (Liability)
        ├─→ 3000 - Equity
        ├─→ 4000 - Sales Revenue
        └─→ 5000 - Cost of Sales
            └─→ JournalEntry (multiple)
                ├─→ JournalEntryLine (2+)
                │   ├─ Debit / Credit Amount
                │   └─ Account reference
                └─→ LinkedTransaction
                    ├─ Sale ID (OrderId)
                    ├─ Inventory TX ID
                    └─ Payment ID

```

---

## 🔐 Authentication & Authorization Flow

```
┌──────────────────────────────────────────────────────┐
│           USER LOGIN (POST /api/auth/login)          │
└──────────────────────────────────────────────────────┘
         Input: { email, password, tenant }
                        │
                        ▼
         ┌─────────────────────────────┐
         │ Find User by Email          │
         │ AND TenantId                │
         └─────────────────────────────┘
                        │
            ┌───────────┴────────────┐
            │ (No user found)        │ (User found)
            ▼                        ▼
         Return 401              ┌────────────────────┐
         Unauthorized            │ Verify Password    │
                                 │ BCrypt.Verify()    │
                                 └────────────────────┘
                                        │
                        ┌───────────────┴────────────────┐
                        │ (Wrong password)               │ (Correct)
                        ▼                                ▼
                     Return 401                    ┌──────────────┐
                     Unauthorized                  │ Create JWT   │
                                                   │ Token        │
                                                   │ Claims:      │
                                                   │ - UserId     │
                                                   │ - TenantId   │
                                                   │ - Role       │
                                                   │ - Email      │
                                                   └──────────────┘
                                                        │
                                                        ▼
                                                   Return 200 OK
                                                   { token, expiry }


┌──────────────────────────────────────────────────────┐
│        SUBSEQUENT REQUEST (with JWT Token)           │
└──────────────────────────────────────────────────────┘
    Header: Authorization: Bearer <JWT_TOKEN>
                        │
                        ▼
         ┌──────────────────────────┐
         │ Validate JWT Signature   │
         │ Check Expiration         │
         └──────────────────────────┘
                        │
            ┌───────────┴────────────┐
            │ (Invalid/Expired)      │ (Valid)
            ▼                        ▼
         Return 401             ┌────────────────────┐
         Unauthorized           │ Extract Claims     │
                                │ - UserId           │
                                │ - TenantId (filter)│
                                │ - Role (authorize) │
                                └────────────────────┘
                                        │
                        ┌───────────────┴────────────────┐
                        │                                │
                        ▼                                ▼
         [Tenant Query Filter]         [Role Check]
         WHERE TenantId = X            if (!User.IsInRole("Admin"))
         Prevents cross-tenant           return Forbid();
         data access                     
                        │
                        ▼
         ┌──────────────────────────┐
         │ Execute Business Logic   │
         │ with Filtered Data       │
         └──────────────────────────┘
                        │
                        ▼
         Return Response (200/201/etc)

ROLES DEFINED:
  - SuperAdmin: All tenants, all operations
  - Admin: Own tenant, full control
  - Staff: Own tenant, limited operations
  - Customer: Own orders/profile only

```

---

## 🏛️ Clean Architecture Principles

```
                 OUTER LAYERS (Infrastructure)
                 ┌─────────────────────────────┐
                 │ Controllers                 │
                 │ Middleware                  │
                 │ EF Core DbContext           │
                 │ Payment Gateways            │
                 │ Email Services              │
                 └────────────────────────────► Independent of domain

                 MIDDLE LAYERS (Application)
                 ┌─────────────────────────────┐
                 │ Services                    │
                 │ DTOs                        │
                 │ Dependency Injection        │
                 └────────────────────────────► Depends on inner

                 INNER LAYER (Domain)
                 ┌─────────────────────────────┐
                 │ Entities                    │
                 │ Value Objects               │
                 │ Business Rules              │
                 │ Exceptions                  │
                 └────────────────────────────► Independent (pure C#)

BENEFIT: Easy to test, maintain, extend
RULE: Dependencies point INWARD only
      Domain → never knows about Controllers
      Services → can use Domain entities
      Controllers → use Services
```

---

## 🚀 Deployment Architecture (Future)

```
┌─────────────────────────────────────────────────────┐
│                   USERS (Internet)                  │
└─────────────────────────────────────────────────────┘
                        │
                        ▼
        ┌────────────────────────────┐
        │    Azure Front Door        │
        │    (CDN + DDoS Protection) │
        └────────────────────────────┘
                        │
          ┌─────────────┴──────────────┐
          ▼                            ▼
┌──────────────────┐        ┌──────────────────┐
│  API Instance 1  │        │  API Instance 2  │
│  (App Service)   │        │  (App Service)   │
└──────────────────┘        └──────────────────┘
          │                            │
          │    ┌──────────────┐       │
          └───→│ Load Balancer├───────┘
               └──────────────┘
                       │
        ┌──────────────┴──────────────┐
        ▼                             ▼
    ┌────────────┐            ┌────────────┐
    │  Blazor UI │            │  Database  │
    │(App Service)            │   (SQL)    │
    └────────────┘            └────────────┘
                                    │
                    ┌───────────────┴───────────────┐
                    ▼                               ▼
            ┌──────────────┐            ┌──────────────┐
            │ Read Replica │            │ Backup       │
            │ (for reports)│            │ (Geo-redundant
            └──────────────┘            └──────────────┘

Additional Services:
  • Redis Cache (Performance)
  • Azure Storage (Blobs for receipts)
  • Azure Service Bus (Background jobs)
  • Application Insights (Monitoring)
  • Key Vault (Secrets)
```

---

## 📦 Dependency Injection Configuration

```
Services Registered in Program.cs:

Infrastructure Layer:
├─ DbContext
│  └─ UseSqlServer(connectionString)
│
├─ Repositories (if using repository pattern)
│
└─ Data Services
   ├─ OrderService
   ├─ InventoryService
   ├─ CustomerService
   ├─ PaymentService
   └─ AccountingService

Cross-Cutting:
├─ Authentication
│  ├─ JWT Bearer handler
│  ├─ JWT generation
│  └─ Current user context
│
├─ Email Service
│  └─ SmtpEmailService
│
├─ Payment Gateway
│  ├─ CashPaymentGateway (current)
│  └─ StripePaymentGateway (future)
│
└─ Logging
   └─ Serilog (to be added)

Middleware Pipeline:
├─ Exception Handling
├─ Logging
├─ CORS
├─ Authentication
├─ Authorization
├─ Tenant Context Resolution
└─ Routing
```

---

## 🔄 Feature Module Structure

```
RetailSuite.Infrastructure/Modules/

Catalog/
├─ Entities/
│  ├─ Product.cs
│  ├─ ProductVariant.cs
│  ├─ Category.cs
│  └─ ProductAttribute.cs
├─ Dtos/
│  ├─ CreateProductRequest.cs
│  ├─ ProductResponse.cs
│  └─ CreateVariantRequest.cs
└─ Services/
   └─ CatalogService.cs

Orders/
├─ Entities/
│  ├─ Order.cs
│  ├─ OrderItem.cs
│  └─ OrderStatus.cs
├─ Dtos/
│  ├─ CreateOrderRequest.cs
│  ├─ CreatePosSaleRequest.cs
│  └─ OrderDto.cs
└─ Services/
   ├─ OrderService.cs
   └─ SaleService.cs

Inventory/
├─ Entities/
│  ├─ InventoryItem.cs
│  ├─ InventoryTransaction.cs
│  └─ InventoryTransactionType.cs
├─ Dtos/
│  ├─ ReceiveStockRequest.cs
│  └─ AdjustStockRequest.cs
└─ Services/
   └─ InventoryService.cs

[Similar for Customers, Identity, Accounting, Payments, Tenants]
```

---

This architecture supports:
✅ Multi-tenancy at every layer
✅ Scalability with stateless API
✅ Clean separation of concerns
✅ Easy testing with dependency injection
✅ Role-based access control
✅ Audit trail via inventory transactions
✅ Financial accuracy via double-entry accounting
✅ Security with JWT + multi-tenant filtering

