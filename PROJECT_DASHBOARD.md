# 📊 RetailSuite Project Dashboard

**Updated**: 13 July 2026 | **Status**: 🟢 Live on Azure, post-launch hardening | **Next**: payment gateway sandbox verification

---

## 🌐 Current Deployment

```
Domain:                www.retailesuite.com (custom domain, managed SSL)
Apex redirect:         retailesuite.com → www (via Namecheap URL Redirect)
Region:                Canada Central (app + DB co-located)
App Service:           RetailSuiteOnline (Basic B1)
SQL Database:          RetailSuiteSuiteDB (Basic tier, Canada Central)
Auth to DB:            SQL admin + Active Directory Default fallback
```

---

## 🟢 Current Code Reality

```
Build:                    ✅ Passing, 0 model warnings
Automated tests:          ✅ 107 / 107 passing, 0 skipped
EF migrations applied:    ✅ Local + Azure in sync
Logging:                  ✅ Serilog → console + rolling file
API documentation:        ✅ Swagger (route collision fixed, JSON loads)
Payment gateways:         🟡 Framework live (Stripe / EasyPaisa / JazzCash) — sandbox verification pending
Email notifications:      ✅ SMTP configured, no-op when host is empty
Storefront ↔ API images:  ✅ ApiUrlService prefixes API base URL
HtmlSanitizer CVE:        ✅ Bumped 8.0.865 → 9.0.892
Secret files:             ✅ Purged from tracking, .gitignore hardened
Production readiness:     🟢 Live and serving — sandbox payment verification is the final gate
```

---

## 🎯 Project Status Overview

```
PHASE 1 — MVP
├─ Multi-tenant core, POS, inventory, orders, users
└─ Status: ✅ COMPLETE

PHASE 2 — HARDENING & PRODUCT DEPTH
├─ Server-side permission enforcement, Reports module, wallet UX polish
├─ Catalog depth: Brand, slug URLs, HTML descriptions, attributes, variant generator, faceted filter
├─ General Ledger: trial balance, per-account ledger, journal entries, manual posting
├─ Blazor navigation: collapsible accordion, 6 groups (Selling, Catalog, Procurement, Reports, Accounting, Settings)
├─ Test suite: 107 / 107 green, integration tests unblocked
└─ Status: ✅ COMPLETE (from a functionality standpoint)

PHASE 3 — PRODUCTION DEPLOYMENT
├─ Azure SQL + App Service, custom domain, managed SSL
├─ Region co-location fix (was Canada app + UAE DB; now both in Canada)
├─ Blazor render-mode + deep-route static-asset fix
└─ Status: ✅ LIVE

POST-LAUNCH HARDENING
├─ Secrets purge + HtmlSanitizer CVE patch  ✅ Done
├─ EF model warnings clean-up               ✅ Done
├─ Test suite green                         ✅ Done
├─ Swagger route collision fix              ✅ Done
├─ DemoDataSeeder InMemory-safe             ✅ Done
├─ Payment gateway sandbox verification     🟡 Open
└─ Status: 🟡 IN PROGRESS
```

---

## 📈 Metrics Dashboard

### Build & Test Quality

```
Metric                            Target       Current      Status
──────────────────────────────────────────────────────────────────
Build errors                      0            0            ✅
Build warnings                    0            0            ✅
EF model warnings                 0            0            ✅
Test pass rate                    100%         107 / 107    ✅
Integration tests enabled         Yes          Yes          ✅
Startup exceptions surfaced       Yes          Yes          ✅ (was swallowed)
Swagger /v1/swagger.json          200          200          ✅ (was 500)
```

### Feature Completeness

```
Feature                           Implemented   Notes
──────────────────────────────────────────────────────────────
Multi-tenant architecture         ✅ 100%       Global query filters + auto-stamp
Product catalog                   ✅ 100%       Brand, slug, HTML desc, UoM, specs, tags
Categories tree                   ✅ 100%       Nested with cycle guard on move
Attributes + variant generator    ✅ 100%       Cartesian product wizard
Storefront                        ✅ 100%       Faceted filter (brand/attr/price), slug URLs
Inventory management              ✅ 100%       Per-location, average cost, rollup
Order management                  ✅ 100%       POS + Online, held sales, layaway
POS                               ✅ 100%       Cash, store credit, loyalty, walk-in fallback
Authentication                    ✅ 100%       JWT, email verify, super-admin bypass
Server-side permission checks     ✅ 100%       [RequirePermission] + PolicyProvider
Admin dashboard                   ✅ 100%       6-group accordion nav
Reports                           ✅ 100%       8-tab: overview, top products, low stock, ...
Wallet (customer)                 ✅ 100%       Store credit + loyalty + OTP
General Ledger                    ✅ 100%       Trial balance, journal entries, manual post
Chart of Accounts                 ✅ 100%       CRUD + defaults seeder
HTML sanitisation                 ✅ 100%       Ganss.Xss 9.0.892 (post-CVE)
Payment framework                 ✅ 100%       Stripe + EasyPaisa + JazzCash factories
Payment gateway sandbox test      🟡 0%         Pending sandbox creds
Email notifications               ✅ 100%       SMTP-configurable, no-op when unset
Logging framework                 ✅ 100%       Serilog + console + rolling file
API documentation                 ✅ 100%       Swagger + JWT bearer
```

---

## 🚀 Recent Activity (this hardening pass)

```
Security
├─ Purged 3 tracked secret files from tracking, expanded .gitignore
└─ HtmlSanitizer 8.0.865 → 9.0.892 (CVE patched)

Test infrastructure
├─ ApiTestFactory: placeholder ConnectionStrings:Default + non-default SuperAdmin:Password
├─ Program.cs: unconditional rethrow of startup exceptions (was swallowed under test host)
├─ Program.cs: skip DemoDataSeeder in Testing env (InMemory quirk)
├─ InventoryServiceTests: seed default Location so ResolveLocationAsync works
└─ Result: 107 / 107 green, 3 integration tests unblocked

Bug fixes
├─ SaleService: flush SaveChangesAsync between IssueStock and rollup SumAsync
│   (EF InMemory doesn't see uncommitted tracked-entity changes in LINQ queries)
├─ ProductController.AddVariant: explicit _db.ProductVariants.Add(v) to sidestep
│   InMemory change-tracker flagging variants as Modified via navigation
├─ DemoDataSeeder: same navigation-add pattern, fixed defensively
└─ Program.cs try/catch: rethrow always (not just when OS-env var says Testing)

EF Core model warnings
├─ ProductCategory / VariantAttributeValue: matching HasQueryFilter mirroring parent
├─ ProductCategory shadow ProductId1 FK: fixed by wiring .WithMany(p => p.Categories)
├─ Migration ModelWarningsFix: IF EXISTS / IF NOT EXISTS guards for cross-DB safety
└─ decimal(18,4) on ProductVariant.AverageCost, decimal(18,2) on Order.PaidAmount

Swagger
├─ Root cause: two controllers claimed [Route("api/attributes")] with 3 colliding actions
└─ Removed stale ProductAttributesController; kept AttributesController (proper guards, DTOs)

Docs / warnings
├─ Escaped & as &amp; in GeneralLedgerController XML comments (CS1570)
└─ Removed orphaned <summary> with stale <paramref name="rootId"/> in ShopController (CS1734)
```

---

## 📋 Open Items

```
Priority   Item                                             Status         Notes
──────────────────────────────────────────────────────────────────────────────
🔴 P0     Rotate secrets that appeared in purged files      🟡 User side   SQL admin, JWT, SuperAdmin pwd
🔴 P0     Payment gateway sandbox smoke tests               🟡 Open        Stripe, EasyPaisa, JazzCash
🟡 P1     Remove DemoDataSeeder "skip in Testing" guard     🟢 Optional    Seeder is now InMemory-safe
🟡 P1     git rm the empty ProductAttributesController.cs   🟢 Cleanup     Neutralised — file exists as stub
🟢 P2     Investigate any residual XML doc warnings         🟢 Watch       0 seen in last build
🟢 P2     PROJECT_DASHBOARD refresh                         ✅ This doc
```

---

## 🔑 Deployment Notes (production references, non-secret)

```
Region locality:
  App Service and SQL Database must stay in the same Azure region.
  Original setup was Canada Central (app) + UAE North (DB); ~11,500 km
  round-trip crushed query latency. Fixed by rebuilding the DB in
  Canada Central.

Custom domain / TLS:
  www.retailesuite.com uses Azure managed SSL (auto-renewing).
  Apex retailesuite.com uses Namecheap URL Redirect → www.
  Namecheap redirect works over HTTP only; the redirect target
  (Azure) enforces HTTPS on the final response. Apex HTTPS is a
  known caveat unless an A-record is used instead.

Blazor render mode:
  Components in the Shared/ folder do NOT inherit InteractiveServer
  cleanly under .NET 8 Blazor Web App. Sidebar accordion is inlined
  into MainLayout.razor for that reason. Keep it inlined.

Static assets under deep routes:
  <base href="/" /> + leading-slash on app.css / blazor.web.js in
  App.razor. Removing these breaks routes like /accounting/... with
  404s on relative asset paths.

Data-integrity defense in depth:
  Three layers stamp Id / CreatedAt / TenantId on new TenantEntity
  rows: (1) BaseEntity property initializer, (2) constructor,
  (3) DbContext.SaveChangesAsync override safety net. Do not remove
  any layer — each catches a different failure mode.
```

---

## ✅ Sign-off Checklist

### Live production readiness
- [x] Multi-tenant isolation verified via global query filters
- [x] Server-side permission enforcement on every protected endpoint
- [x] All secrets rotated out of tracked files
- [x] HtmlSanitizer patched past disclosed CVE
- [x] 107 / 107 automated tests green
- [x] EF migrations applied to Azure and local in sync
- [x] Swagger /swagger loads (route collision resolved)
- [x] Deployed to Azure with managed SSL on www.retailesuite.com
- [ ] Payment gateway sandbox smoke tests passed
- [ ] SQL admin, JWT signing key, SuperAdmin password rotated after secret-purge

### Nice-to-haves
- [ ] Remove DemoDataSeeder Testing-env skip (seeder is now safe)
- [ ] git rm empty ProductAttributesController stub
- [ ] Load-test the storefront during peak
- [ ] PDF receipt generation
- [ ] Advanced business reports beyond the current 8 tabs

---

## 📞 Support & References

```
Custom domain / DNS       Namecheap dashboard
App Service               Azure Portal → RetailSuiteOnline
SQL Database              Azure Portal → RetailSuiteSuiteDB (Canada Central)
Logs                      App Service → Log Stream, plus rolling files under logs/
Code review baseline      Latest Codex audit + this dashboard
Storefront                https://www.retailesuite.com
Admin panel               (login required) — same host
```

---

**Last Updated**: 13 July 2026
**Status**: 🟢 Live on Azure — final gate is payment gateway sandbox verification
