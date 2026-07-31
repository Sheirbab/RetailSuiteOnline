using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Email;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Customer.Services;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Payments.Entities;
using RetailSuite.Infrastructure.Modules.Payments.Services;
using RetailSuite.Infrastructure.Modules.Shipping.Entities;
using RetailSuite.Infrastructure.Modules.Tax.Services;
using RetailSuite.Infrastructure.Seeders;
using RetailSuite.Modules.Accounting.Services;
using RetailSuite.Modules.Catalog.Entities;
using RetailSuite.Modules.Orders.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Public storefront endpoints (no auth required). Returns sanitised data only —
/// never raw entities. Used by the /shop Blazor pages and any future PWA / mobile client.
/// Every route is scoped under {tenantSlug} (the tenant's Subdomain value used as a path
/// segment) — <see cref="RetailSuite.Api.MultiTenancy.ResolveShopTenantFilter"/> resolves it
/// to a TenantId before the action runs, so all EF Core tenant-scoped queries below are
/// correctly scoped to that one store rather than leaking data across tenants.
/// </summary>
[ApiController]
[Route("api/shop/{tenantSlug}")]
[AllowAnonymous]
[ServiceFilter(typeof(RetailSuite.Api.MultiTenancy.ResolveShopTenantFilter))]
public class ShopController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly AccountingService _accounting;
    private readonly IEmailService _email;
    private readonly ITenantContext _tenantContext;
    private readonly IInvoiceStampingService _invoiceStamper;
    private readonly IOrderPaymentService _paymentService;
    private readonly IStoreCreditService _storeCredit;
    private readonly IConfiguration _config;

    public ShopController(
        RetailDbContext db,
        AccountingService accounting,
        IEmailService email,
        ITenantContext tenantContext,
        IInvoiceStampingService invoiceStamper,
        IOrderPaymentService paymentService,
        IStoreCreditService storeCredit,
        IConfiguration config)
    {
        _db = db;
        _accounting = accounting;
        _email = email;
        _tenantContext = tenantContext;
        _invoiceStamper = invoiceStamper;
        _paymentService = paymentService;
        _storeCredit   = storeCredit;
        _config        = config;
    }

    // ============================================================
    //  GET /api/shop/categories
    // ============================================================
    /// <summary>
    /// Public category list for the storefront sidebar / filter.
    /// Returns categories with a product count so the UI can show "(12)".
    /// </summary>
    [HttpGet("categories")]
    public async Task<IActionResult> Categories(string tenantSlug)
    {
        // Count distinct products per category via the join table.
        var counts = await _db.ProductCategories
            .GroupBy(pc => pc.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count);

        var rows = await _db.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync();

        // Build a flat enriched list with parent ids (keeps back-compat with the
        // existing flat sidebar), plus a nested Tree property so newer UI can
        // render hierarchy without a second round-trip.
        var flat = rows.Select(c => new
        {
            c.Id, c.Name, c.Slug, c.ParentCategoryId,
            ProductCount = counts.TryGetValue(c.Id, out var n) ? n : 0
        }).ToList();

        // Tree of all-descendants counts so the storefront can show "(45)" on a
        // parent that aggregates every product under its subtree.
        var byId       = rows.ToDictionary(c => c.Id);
        var childrenOf = rows
            .Where(c => c.ParentCategoryId.HasValue)
            .GroupBy(c => c.ParentCategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        int CountIncludingDescendants(Guid id)
        {
            var total = counts.TryGetValue(id, out var n) ? n : 0;
            if (childrenOf.TryGetValue(id, out var kids))
                foreach (var k in kids) total += CountIncludingDescendants(k.Id);
            return total;
        }

        object MakeNode(Modules.Catalog.Entities.Category c) => new
        {
            c.Id, c.Name, c.Slug, c.ParentCategoryId,
            ProductCount             = counts.TryGetValue(c.Id, out var n) ? n : 0,
            ProductCountWithChildren = CountIncludingDescendants(c.Id),
            Children = childrenOf.TryGetValue(c.Id, out var kids)
                ? kids.Select(MakeNode).ToList()
                : new List<object>()
        };

        var tree = rows
            .Where(c => !c.ParentCategoryId.HasValue)
            .Select(MakeNode)
            .ToList();

        return Ok(ApiResponse<object>.Ok(new { Flat = flat, Tree = tree }));
    }

    // ============================================================
    //  GET /api/shop/products?categoryId=&search=&page=
    // ============================================================
    /// <summary>
    /// Public product listing for the storefront. Filter by category and / or search term.
    /// Returns flat product cards with primary image + min variant price.
    /// </summary>
    [HttpGet("products")]
    public async Task<IActionResult> Products(
        string tenantSlug,
        [FromQuery] Guid? categoryId,
        [FromQuery] string? search,
        [FromQuery] string? brandIds,
        [FromQuery] string? attrValueIds,
        [FromQuery] decimal? priceMin,
        [FromQuery] decimal? priceMax,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Products
            .Include(p => p.Variants)
            .Where(p => p.IsActive)
            .AsQueryable();

        if (categoryId.HasValue && categoryId.Value != Guid.Empty)
        {
            // Include products in this category AND any descendant categories so a
            // shopper clicking "Electronics" sees items tagged "Audio > Headphones" too.
            var descendants = await GetDescendantCategoryIdsAsync(categoryId.Value);
            descendants.Add(categoryId.Value);
            var productIds = _db.ProductCategories
                .Where(pc => descendants.Contains(pc.CategoryId))
                .Select(pc => pc.ProductId);
            query = query.Where(p => productIds.Contains(p.Id));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(p =>
                p.Name.Contains(s) ||
                p.Variants.Any(v => v.SKU.Contains(s) || (v.Barcode != null && v.Barcode.Contains(s))));
        }

        // Brand filter — OR semantics within the parameter (any-of).
        var brandList = ParseGuidList(brandIds);
        if (brandList.Count > 0)
            query = query.Where(p => p.BrandId.HasValue && brandList.Contains(p.BrandId.Value));

        // Attribute filter — AND across attribute groups, OR within values of a group.
        // We resolve the values to their attribute ids and require at least one match per group.
        var attrValueList = ParseGuidList(attrValueIds);
        if (attrValueList.Count > 0)
        {
            var valueToAttr = await _db.ProductAttributeValues
                .Where(v => attrValueList.Contains(v.Id))
                .Select(v => new { v.Id, v.AttributeId })
                .ToListAsync();
            var groups = valueToAttr.GroupBy(v => v.AttributeId).ToList();

            foreach (var g in groups)
            {
                var vids = g.Select(x => x.Id).ToList();
                query = query.Where(p =>
                    p.Variants.Any(v =>
                        _db.VariantAttributeValues.Any(va =>
                            va.ProductVariantId == v.Id && vids.Contains(va.ProductAttributeValueId))));
            }
        }

        // Price filter — at least one active variant within range.
        if (priceMin.HasValue)
            query = query.Where(p => p.Variants.Any(v => v.IsActive && v.Price >= priceMin.Value));
        if (priceMax.HasValue)
            query = query.Where(p => p.Variants.Any(v => v.IsActive && v.Price <= priceMax.Value));

        var total = await query.CountAsync();

        var products = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                p.ShortDescription,
                p.ImageUrl,
                MinPrice     = p.Variants.Where(v => v.IsActive).Min(v => (decimal?)v.Price) ?? 0m,
                VariantCount = p.Variants.Count(v => v.IsActive),
                BrandName    = _db.Brands.Where(b => b.Id == p.BrandId).Select(b => b.Name).FirstOrDefault()
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            Total = total, Page = page, PageSize = pageSize, Items = products
        }));
    }

    // ============================================================
    //  GET /api/shop/products/{id}
    // ============================================================
    /// <summary>Product detail with active variants — used by the storefront product page.</summary>
    [HttpGet("products/{id:guid}")]
    public async Task<IActionResult> ProductDetail(string tenantSlug, Guid id) => await BuildProductDetailAsync(p => p.Id == id);

    /// <summary>
    /// Slug-based lookup for the storefront — e.g. /api/shop/{tenantSlug}/products/by-slug/blue-cotton-shirt.
    /// Used by /shop/p/{slug} so URLs are SEO friendly and shareable.
    /// </summary>
    [HttpGet("products/by-slug/{slug}")]
    public async Task<IActionResult> ProductDetailBySlug(string tenantSlug, string slug)
        => await BuildProductDetailAsync(p => p.Slug == slug);

    private async Task<IActionResult> BuildProductDetailAsync(System.Linq.Expressions.Expression<Func<Product, bool>> filter)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .Where(filter)
            .Where(p => p.IsActive)
            .Select(p => new
            {
                p.Id, p.Name, p.Slug, p.Description, p.ShortDescription, p.ImageUrl,
                p.UnitOfMeasure, p.Specs, p.Tags,
                Brand = _db.Brands
                    .Where(b => b.Id == p.BrandId && b.IsActive)
                    .Select(b => new { b.Id, b.Name, b.Slug, b.LogoUrl })
                    .FirstOrDefault(),
                Categories = _db.ProductCategories
                    .Where(pc => pc.ProductId == p.Id)
                    .Join(_db.Categories,
                          pc => pc.CategoryId,
                          c  => c.Id,
                          (pc, c) => new { c.Id, c.Name, c.Slug, c.ParentCategoryId })
                    .ToList(),
                Variants = p.Variants
                    .Where(v => v.IsActive)
                    .Select(v => new
                    {
                        v.Id, v.SKU, v.Price, v.StockQuantity, v.Barcode, v.TaxRate,
                        // Each variant's (attribute → value) pairs — e.g. Size=M, Color=Red.
                        Attributes = _db.VariantAttributeValues
                            .Where(va => va.ProductVariantId == v.Id)
                            .Join(_db.ProductAttributeValues,
                                  va => va.ProductAttributeValueId,
                                  pav => pav.Id,
                                  (va, pav) => new { pav.AttributeId, pav.Value })
                            .Join(_db.ProductAttributes,
                                  x => x.AttributeId,
                                  a => a.Id,
                                  (x, a) => new { AttributeName = a.Name, x.Value })
                            .ToList()
                    }).ToList(),
                Images = _db.ProductImages
                    .Where(i => i.ProductId == p.Id)
                    .OrderBy(i => i.SortOrder)
                    .Select(i => new { i.Id, Url = i.RelativePath, i.IsPrimary })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        if (product == null)
            return NotFound(ApiResponse<object>.Fail("Product not found."));

        return Ok(ApiResponse<object>.Ok(product));
    }

    // ============================================================
    //  GET /api/shop/shipping-methods?subtotal=
    // ============================================================
    /// <summary>
    /// Active shipping methods for the current tenant. Pass <paramref name="subtotal"/> to get
    /// the computed fee per method (accounting for free-over-threshold rules).
    /// </summary>
    [HttpGet("shipping-methods")]
    public async Task<IActionResult> ShippingMethods(string tenantSlug, [FromQuery] decimal subtotal = 0m)
    {
        var rows = await _db.ShippingMethods
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        var methods = rows.Select(m => new
        {
            m.Id, m.Code, m.Name, m.Description, m.Eta,
            m.IsPickup, m.BaseFee, m.FreeOverAmount,
            Fee = m.FeeFor(subtotal)
        });

        return Ok(ApiResponse<object>.Ok(methods));
    }

    // ============================================================
    //  POST /api/shop/checkout
    // ============================================================
    /// <summary>
    /// Guest (or logged-in) checkout. Creates the Order, decrements inventory,
    /// posts the GL journal for the cash portion (zero for COD since cash is collected
    /// on delivery), and emails a confirmation. Returns the order number for tracking.
    /// </summary>
    /// <remarks>
    /// For COD, no Payment row is recorded yet (PaidAmount stays 0). When the courier
    /// returns the cash, admin marks the order paid via the existing /api/payments endpoint.
    /// </remarks>
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(string tenantSlug, [FromBody] GuestCheckoutRequest request)
    {
        if (request.Items == null || request.Items.Count == 0)
            return BadRequest(ApiResponse<object>.Fail("Cart is empty."));

        if (string.IsNullOrWhiteSpace(request.GuestName) || string.IsNullOrWhiteSpace(request.GuestPhone))
            return BadRequest(ApiResponse<object>.Fail("Name and phone are required."));

        if (string.IsNullOrWhiteSpace(request.ShippingMethodCode))
            return BadRequest(ApiResponse<object>.Fail("Shipping method is required."));

        if (string.IsNullOrWhiteSpace(request.PaymentMethod))
            return BadRequest(ApiResponse<object>.Fail("Payment method is required."));

        var tenantId = _tenantContext.TenantId
            ?? throw new BusinessRuleException("Tenant context missing on storefront request.");

        var shipping = await _db.ShippingMethods
            .FirstOrDefaultAsync(s => s.Code == request.ShippingMethodCode.ToUpperInvariant() && s.IsActive);

        if (shipping == null)
            return BadRequest(ApiResponse<object>.Fail("Unknown shipping method."));

        // Pickup overrides the address requirement.
        if (!shipping.IsPickup && request.ShippingAddress == null)
            return BadRequest(ApiResponse<object>.Fail("Shipping address is required for delivery methods."));

        // ---- 0. Wallet token (optional) — when present, the order belongs to a
        //         verified customer who may redeem store credit at checkout.
        Guid? walletCustomerId = null;
        if (!string.IsNullOrWhiteSpace(request.WalletToken))
        {
            var decoded = DecodeWalletToken(request.WalletToken);
            if (decoded == null)
                return Unauthorized(ApiResponse<object>.Fail("Wallet session expired — sign in again."));
            if (decoded.Value.TenantId != tenantId)
                return BadRequest(ApiResponse<object>.Fail("Wallet token does not belong to this store."));
            walletCustomerId = decoded.Value.CustomerId;
        }

        if (request.StoreCreditApply > 0 && walletCustomerId == null)
            return BadRequest(ApiResponse<object>.Fail("Wallet sign-in required to redeem store credit."));

        Order? order = null;
        decimal shippingFee = 0;
        decimal amountDue = 0;

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // ---- 1. Build order header
            var orderNumber = $"WEB-{DateTime.UtcNow.Ticks}";
            // Guest orders have no real CustomerId — fall back to the tenant's
            // auto-seeded Walk-in Customer row so the Order FK is satisfied.
            // A wallet-signed-in customer takes precedence over a guest body field.
            var hasRealCustomer = walletCustomerId.HasValue
                               || (request.CustomerId.HasValue && request.CustomerId.Value != Guid.Empty);
            var customerId  = walletCustomerId
                ?? (request.CustomerId.HasValue && request.CustomerId.Value != Guid.Empty
                    ? request.CustomerId.Value
                    : await TenantDefaultsSeeder.GetWalkInCustomerIdAsync(_db, tenantId));
            order = new Order(orderNumber, customerId);
            // Stamp TenantId immediately — the invoice stamper (below) needs it to compute
            // the invoice sequence; the SaveChangesAsync belt-and-braces stamp runs too late.
            order.TenantId = tenantId;
            order.SetChannel("Online");
            order.SetGuestContact(request.GuestName.Trim(), request.GuestPhone.Trim(), request.GuestEmail?.Trim());
            order.SetPaymentMethod(request.PaymentMethod.Trim());

            // ---- 2. Add items + decrement inventory
            decimal totalCogs = 0;
            foreach (var line in request.Items)
            {
                var variant = await _db.ProductVariants.FirstOrDefaultAsync(v => v.Id == line.ProductVariantId);
                if (variant == null)
                    throw new BusinessRuleException($"Unknown variant {line.ProductVariantId}.");

                var inventory = await _db.InventoryItems.FirstAsync(i => i.ProductVariantId == variant.Id);
                if (inventory.CurrentStock < line.Quantity)
                    throw new BusinessRuleException($"Insufficient stock for {variant.SKU}.");

                var costAmount = inventory.IssueStock(line.Quantity);
                variant.StockQuantity = inventory.CurrentStock;
                variant.AverageCost   = inventory.AverageCost;
                totalCogs += costAmount;

                order.AddItem(new OrderItem(
                    order.Id, variant.Id, variant.SKU,
                    variant.Price, line.Quantity, variant.TaxRate));

                _db.InventoryTransactions.Add(new InventoryTransaction(
                    inventory.Id, variant.Id, inventory.LocationId,
                    -line.Quantity,
                    InventoryTransactionType.Sale, order.Id.ToString(), "Online sale"));
            }

            // ---- 3. Shipping
            shippingFee = shipping.FeeFor(order.TotalAmount);
            var addressJson = shipping.IsPickup
                ? "null"
                : JsonSerializer.Serialize(request.ShippingAddress);
            order.SetShipping(shipping.Code, shippingFee, addressJson);

            // ---- 4. Confirm. For COD we don't complete or register a payment yet —
            //         the order sits in Confirmed/Pending fulfillment until courier returns cash.
            //         FBR invoice is stamped at Confirm time since the document is needed for delivery.
            order.Confirm();
            await _invoiceStamper.StampAsync(order);
            _db.Orders.Add(order);

            // ---- 4b. Wallet redemption — book the negative store-credit ledger entry
            //          and stamp the redemption snapshot on the order. Throws if the
            //          customer's balance is insufficient. Walk-in customers can never
            //          reach this branch (gated above on walletCustomerId).
            if (request.StoreCreditApply > 0 && walletCustomerId.HasValue)
            {
                // Cap the redemption at the order total so we never go negative on AR.
                var maxApply  = order.TotalAmount;
                var applying  = Math.Min(request.StoreCreditApply, maxApply);

                await _storeCredit.RedeemAsync(
                    tenantId, walletCustomerId.Value, applying,
                    order.Id, null, $"Online sale {order.OrderNumber}");

                order.ApplyStoreCreditRedemption(applying);
            }

            // ---- 5. Accounting — book inventory issue (COGS) + AR for the net amount the
            //         customer still owes after wallet redemption. The redeemed portion
            //         is tracked in StoreCreditTransactions; treated as discount in GL.
            // Self-heal: ensure baseline Chart of Accounts exists. Idempotent.
            await TenantDefaultsSeeder.SeedAsync(_db, tenantId);

            var inventoryAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1100");
            var cogsAccount      = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "5000");
            var arAccount        = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1200");
            var revenueAccount   = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "4000");
            if (inventoryAccount == null || cogsAccount == null || arAccount == null || revenueAccount == null)
                throw new BusinessRuleException(
                    "Chart of Accounts is incomplete for this tenant (1100 / 5000 / 1200 / 4000).");

            // Amount the customer still owes (= TotalAmount - StoreCreditRedeemed - LoyaltyRedeemedRupees).
            // For online checkout this is what shows up in AR and on the payment QR / COD slip.
            amountDue       = order.AmountDueAfterRedemptions;
            // Proportional split of tax across what the customer actually owes, mirroring POS.
            var dueRatio    = order.TotalAmount > 0
                ? Math.Min(1m, amountDue / order.TotalAmount)
                : 0m;
            var dueTax      = order.TaxAmount * dueRatio;
            var dueRevenue  = amountDue - dueTax;

            var journalLines = new List<(Guid, decimal, decimal)>();
            if (amountDue > 0)
            {
                journalLines.Add((arAccount.Id,      amountDue, 0));
                journalLines.Add((revenueAccount.Id, 0, dueRevenue));
            }
            // COGS posts even when redemption pays the entire order — inventory still moved.
            journalLines.Add((cogsAccount.Id,      totalCogs, 0));
            journalLines.Add((inventoryAccount.Id, 0, totalCogs));

            if (dueTax > 0)
            {
                var taxAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "2000");
                if (taxAccount != null) journalLines.Add((taxAccount.Id, 0, dueTax));
            }

            await _accounting.CreateJournalEntryAsync(
                order.Id.ToString(),
                $"Online sale {order.OrderNumber}",
                journalLines);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
        });

        // ---- 5b. Payment intent for QR-based providers (EasyPaisa / JazzCash).
        //         Intent amount must reflect the net amount owed AFTER wallet redemption.
        //         If wallet fully covers the order, no QR is needed.
        OrderPaymentIntent? paymentIntent = null;
        var providerLower = (order!.PaymentMethodCode ?? "").ToLowerInvariant();
        if (providerLower is "easypaisa" or "jazzcash" && amountDue > 0)
        {
            paymentIntent = await _paymentService.CreateIntentAsync(
                order.Id, order.PaymentMethodCode!, amountDue);
        }

        // ---- 6. Email confirmation (best-effort)
        if (!string.IsNullOrWhiteSpace(order.GuestEmail))
        {
            var totalLine    = $"<p><strong>Total:</strong> Rs {order.TotalAmount:N2}</p>";
            var shippingLine = order.ShippingAmount > 0
                ? $"<p><strong>Shipping:</strong> Rs {order.ShippingAmount:N2} ({shipping.Name})</p>"
                : $"<p><strong>Shipping:</strong> Free ({shipping.Name})</p>";

            var body = $@"
                    <h2>Order confirmation — {order.OrderNumber}</h2>
                    <p>Thank you for your order, {System.Net.WebUtility.HtmlEncode(order.GuestName)}.</p>
                    {totalLine}
                    {shippingLine}
                    <p><strong>Payment:</strong> {order.PaymentMethodCode}</p>
                    <p>Track your order at our store using order number <strong>{order.OrderNumber}</strong>
                    and the phone number <strong>{order.GuestPhone}</strong>.</p>";

            try
            {
                await _email.SendAsync(order.GuestEmail, $"Order confirmed — {order.OrderNumber}", body);
            }
            catch { /* email is best-effort */ }
        }

        return Ok(ApiResponse<object>.Ok(new
        {
            order.OrderNumber,
            order.TotalAmount,
            ShippingFee     = shippingFee,
            PaymentMethod   = order.PaymentMethodCode,
            ShippingMethod  = shipping.Name,
            IsPickup        = shipping.IsPickup,
            ExpectedDelivery = shipping.Eta,
            StoreCreditRedeemed = order.StoreCreditRedeemed,
            AmountDue       = amountDue,
            // Present only for QR-based providers AND when the customer still owes
            // something after wallet redemption.
            Payment = paymentIntent == null ? null : new
            {
                IntentId  = paymentIntent.Id,
                Provider  = paymentIntent.Provider,
                AmountDue = paymentIntent.AmountDue,
                QrPayload = paymentIntent.QrPayload,
                ExpiresAt = paymentIntent.ExpiresAt,
                QrImageUrl = $"/api/payments/qr/{paymentIntent.Id}.png"
            }
        }));
    }

    // ============================================================
    //  GET /api/shop/orders/{orderNumber}?phone=
    // ============================================================
    /// <summary>
    /// Guest order tracking. Customer types order number + the phone they used at checkout —
    /// we match both to prevent enumeration. Returns status + fulfillment progress.
    /// </summary>
    [HttpGet("orders/{orderNumber}")]
    public async Task<IActionResult> Track(string tenantSlug, string orderNumber, [FromQuery] string phone)
    {
        if (string.IsNullOrWhiteSpace(orderNumber) || string.IsNullOrWhiteSpace(phone))
            return BadRequest(ApiResponse<object>.Fail("Order number and phone are required."));

        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber.Trim() && o.GuestPhone == phone.Trim());

        if (order == null)
            return NotFound(ApiResponse<object>.Fail("No order matched that combination."));

        // Find any active payment intent (Pending, not expired) so the page can re-show the QR.
        var activeIntent = await _db.OrderPaymentIntents
            .Where(i => i.OrderId == order.Id && i.Status == PaymentIntentStatus.Pending)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new
            {
                i.Id,
                i.Provider,
                i.AmountDue,
                i.ExpiresAt,
                i.QrPayload
            })
            .FirstOrDefaultAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            Id = order.Id,
            order.OrderNumber,
            order.InvoiceNumber,
            Status            = order.Status.ToString(),
            FulfillmentStatus = order.FulfillmentStatus,
            order.TotalAmount,
            order.PaidAmount,
            Outstanding       = order.OutstandingAmount,
            IsFullyPaid       = order.IsFullyPaid,
            order.ShippingAmount,
            order.ShippingMethodCode,
            order.PaymentMethodCode,
            order.GuestName,
            order.CreatedAt,
            Items = order.Items.Select(i => new { i.SKU, i.Quantity, i.UnitPrice, LineTotal = i.LineTotal }),
            PendingPayment = activeIntent == null ? null : new
            {
                IntentId   = activeIntent.Id,
                activeIntent.Provider,
                activeIntent.AmountDue,
                activeIntent.ExpiresAt,
                QrImageUrl = $"/api/payments/qr/{activeIntent.Id}.png"
            }
        }));
    }

    // ============================================================
    //  GET /api/shop/filters?categoryId=&search=
    //  Returns the available facets for the current product scope:
    //  brand counts, attribute-value counts, and price min/max.
    // ============================================================
    [HttpGet("filters")]
    public async Task<IActionResult> Filters(
        string tenantSlug,
        [FromQuery] Guid? categoryId,
        [FromQuery] string? search)
    {
        // Resolve the base product set (same scope as /products, no facet filters).
        var query = _db.Products
            .Include(p => p.Variants)
            .Where(p => p.IsActive)
            .AsQueryable();

        if (categoryId.HasValue && categoryId.Value != Guid.Empty)
        {
            var descendants = await GetDescendantCategoryIdsAsync(categoryId.Value);
            descendants.Add(categoryId.Value);
            var productIds = _db.ProductCategories
                .Where(pc => descendants.Contains(pc.CategoryId))
                .Select(pc => pc.ProductId);
            query = query.Where(p => productIds.Contains(p.Id));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(p =>
                p.Name.Contains(s) ||
                p.Variants.Any(v => v.SKU.Contains(s) || (v.Barcode != null && v.Barcode.Contains(s))));
        }

        var productIdsForScope = await query.Select(p => p.Id).ToListAsync();

        // Brand counts
        var brandCounts = await _db.Products
            .Where(p => productIdsForScope.Contains(p.Id) && p.BrandId.HasValue)
            .GroupBy(p => p.BrandId!.Value)
            .Select(g => new { BrandId = g.Key, Count = g.Count() })
            .ToListAsync();
        var brands = await _db.Brands.Where(b => b.IsActive).ToListAsync();
        var brandFacets = brands.Select(b => new
        {
            b.Id, b.Name, b.Slug,
            Count = brandCounts.FirstOrDefault(c => c.BrandId == b.Id)?.Count ?? 0
        }).Where(x => x.Count > 0).OrderBy(x => x.Name).ToList();

        // Attribute facets: { Name, Values: [{Id, Value, Count}] }
        var variantIds = await _db.ProductVariants
            .Where(v => productIdsForScope.Contains(v.ProductId) && v.IsActive)
            .Select(v => v.Id)
            .ToListAsync();
        var attrPairs = await _db.VariantAttributeValues
            .Where(va => variantIds.Contains(va.ProductVariantId))
            .Join(_db.ProductAttributeValues,
                  va => va.ProductAttributeValueId,
                  pav => pav.Id,
                  (va, pav) => new { va.ProductVariantId, pav.Id, pav.AttributeId, pav.Value })
            .ToListAsync();
        var attrLookup = await _db.ProductAttributes.ToDictionaryAsync(a => a.Id, a => a.Name);
        var attrFacets = attrPairs
            .GroupBy(x => x.AttributeId)
            .Where(g => attrLookup.ContainsKey(g.Key))
            .Select(g => new
            {
                AttributeId = g.Key,
                Name        = attrLookup[g.Key],
                Values = g.GroupBy(x => x.Id)
                    .Select(vg => new
                    {
                        Id    = vg.Key,
                        Value = vg.First().Value,
                        // Distinct product count for this value within the scope.
                        Count = vg.Select(x => x.ProductVariantId).Distinct().Count()
                    })
                    .OrderBy(x => x.Value)
                    .ToList()
            })
            .OrderBy(g => g.Name)
            .ToList();

        // Price range across active variants in scope.
        var priceQ = _db.ProductVariants
            .Where(v => productIdsForScope.Contains(v.ProductId) && v.IsActive);
        decimal? priceMin = await priceQ.AnyAsync() ? await priceQ.MinAsync(v => (decimal?)v.Price) : null;
        decimal? priceMax = await priceQ.AnyAsync() ? await priceQ.MaxAsync(v => (decimal?)v.Price) : null;

        return Ok(ApiResponse<object>.Ok(new
        {
            Brands     = brandFacets,
            Attributes = attrFacets,
            Price      = new { Min = priceMin ?? 0m, Max = priceMax ?? 0m }
        }));
    }

    /// <summary>Parses a comma-separated list of GUIDs from a query param. Skips invalid entries.</summary>
    private static List<Guid> ParseGuidList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new();
        var list = new List<Guid>();
        foreach (var token in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(token, out var g)) list.Add(g);
        }
        return list;
    }

    private async Task<HashSet<Guid>> GetDescendantCategoryIdsAsync(Guid rootId)
    {
        var pairs = await _db.Categories
            .Where(c => c.ParentCategoryId != null)
            .Select(c => new { c.Id, ParentId = c.ParentCategoryId!.Value })
            .ToListAsync();

        var childrenOf = pairs
            .GroupBy(p => p.ParentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        var result = new HashSet<Guid>();
        var stack  = new Stack<Guid>();
        stack.Push(rootId);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!childrenOf.TryGetValue(cur, out var kids)) continue;
            foreach (var k in kids)
            {
                if (result.Add(k)) stack.Push(k);
            }
        }
        return result;
    }

    // ============================================================
    //  Wallet-token helper
    // ============================================================
    /// <summary>
    /// Validates a wallet JWT issued by /api/wallet/otp/verify and returns the
    /// embedded customer + tenant ids. Returns null when the token is invalid,
    /// expired, or missing required claims.
    /// </summary>
    private (Guid CustomerId, Guid TenantId)? DecodeWalletToken(string token)
    {
        try
        {
            var jwt = _config.GetSection("Jwt");
            var keyBytes = Encoding.UTF8.GetBytes(jwt["Key"] ?? "");
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidIssuer              = jwt["Issuer"],
                ValidateAudience         = true,
                ValidAudience            = jwt["Audience"],
                ValidateLifetime         = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = new SymmetricSecurityKey(keyBytes),
                ClockSkew                = TimeSpan.FromSeconds(30)
            };

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, parameters, out _);

            var customerClaim = principal.FindFirst("customer_id")?.Value;
            var tenantClaim   = principal.FindFirst("tenantId")?.Value;
            if (!Guid.TryParse(customerClaim, out var cid)) return null;
            if (!Guid.TryParse(tenantClaim,   out var tid)) return null;

            // Must be a wallet token (not a staff JWT).
            var role = principal.FindFirst(ClaimTypes.Role)?.Value;
            if (role != "WalletCustomer") return null;

            return (cid, tid);
        }
        catch
        {
            return null;
        }
    }
}

// ----- Storefront DTOs ----------------------------------------------

public class GuestCheckoutRequest
{
    /// <summary>Optional — if the caller is a logged-in customer, attach them. Null for guest.</summary>
    public Guid? CustomerId { get; set; }

    public string GuestName { get; set; } = string.Empty;
    public string GuestPhone { get; set; } = string.Empty;
    public string? GuestEmail { get; set; }

    public List<GuestCheckoutLine> Items { get; set; } = new();

    /// <summary>ShippingMethod.Code — must match a configured active method.</summary>
    public string ShippingMethodCode { get; set; } = string.Empty;

    /// <summary>Required unless the chosen shipping method is pickup-only.</summary>
    public GuestShippingAddress? ShippingAddress { get; set; }

    /// <summary>"COD" | "BankTransfer" | future: "EasyPaisa", "JazzCash", "Stripe".</summary>
    public string PaymentMethod { get; set; } = "COD";

    /// <summary>
    /// Optional wallet JWT (issued by /api/wallet/otp/verify). When present the order
    /// is linked to the verified customer and store credit / loyalty may be redeemed.
    /// </summary>
    public string? WalletToken { get; set; }

    /// <summary>Amount of store credit to redeem against this sale. Requires WalletToken.</summary>
    public decimal StoreCreditApply { get; set; }
}

public class GuestCheckoutLine
{
    public Guid ProductVariantId { get; set; }
    public int  Quantity { get; set; }
}

public class GuestShippingAddress
{
    public string RecipientName { get; set; } = string.Empty;
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? Province { get; set; }
    public string? PostalCode { get; set; }
    public string Country { get; set; } = "PK";
    public string? Phone { get; set; }
    public string? DeliveryInstructions { get; set; }
}
