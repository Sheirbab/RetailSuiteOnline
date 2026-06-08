using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Email;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Shipping.Entities;
using RetailSuite.Infrastructure.Modules.Tax.Services;
using RetailSuite.Modules.Accounting.Services;
using RetailSuite.Modules.Orders.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Public storefront endpoints (no auth required). Returns sanitised data only —
/// never raw entities. Used by the /shop Blazor pages and any future PWA / mobile client.
/// </summary>
[ApiController]
[Route("api/shop")]
[AllowAnonymous]
public class ShopController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly AccountingService _accounting;
    private readonly IEmailService _email;
    private readonly ITenantContext _tenantContext;
    private readonly IInvoiceStampingService _invoiceStamper;

    public ShopController(
        RetailDbContext db,
        AccountingService accounting,
        IEmailService email,
        ITenantContext tenantContext,
        IInvoiceStampingService invoiceStamper)
    {
        _db = db;
        _accounting = accounting;
        _email = email;
        _tenantContext = tenantContext;
        _invoiceStamper = invoiceStamper;
    }

    // ============================================================
    //  GET /api/shop/categories
    // ============================================================
    /// <summary>
    /// Public category list for the storefront sidebar / filter.
    /// Returns categories with a product count so the UI can show "(12)".
    /// </summary>
    [HttpGet("categories")]
    public async Task<IActionResult> Categories()
    {
        // Count distinct products per category via the join table.
        var counts = await _db.ProductCategories
            .GroupBy(pc => pc.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count);

        var rows = await _db.Categories
            .OrderBy(c => c.Name)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Slug,
                c.ParentCategoryId
            })
            .ToListAsync();

        var enriched = rows.Select(c => new
        {
            c.Id, c.Name, c.Slug, c.ParentCategoryId,
            ProductCount = counts.TryGetValue(c.Id, out var n) ? n : 0
        });

        return Ok(ApiResponse<object>.Ok(enriched));
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
        [FromQuery] Guid? categoryId,
        [FromQuery] string? search,
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
            var productIds = _db.ProductCategories
                .Where(pc => pc.CategoryId == categoryId.Value)
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

        var total = await query.CountAsync();

        var products = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.ImageUrl,
                MinPrice = p.Variants.Where(v => v.IsActive).Min(v => (decimal?)v.Price) ?? 0m,
                VariantCount = p.Variants.Count(v => v.IsActive)
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
    public async Task<IActionResult> ProductDetail(Guid id)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .Where(p => p.Id == id && p.IsActive)
            .Select(p => new
            {
                p.Id, p.Name, p.Description, p.ImageUrl,
                Variants = p.Variants
                    .Where(v => v.IsActive)
                    .Select(v => new
                    {
                        v.Id, v.SKU, v.Price, v.StockQuantity, v.Barcode, v.TaxRate
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
    public async Task<IActionResult> ShippingMethods([FromQuery] decimal subtotal = 0m)
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
    public async Task<IActionResult> Checkout([FromBody] GuestCheckoutRequest request)
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

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // ---- 1. Build order header
            var orderNumber = $"WEB-{DateTime.UtcNow.Ticks}";
            // Guest orders have no real CustomerId — pass Guid.Empty.
            var customerId  = request.CustomerId ?? Guid.Empty;
            var order = new Order(orderNumber, customerId);
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
            var shippingFee = shipping.FeeFor(order.TotalAmount);
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

            // ---- 5. Accounting — book inventory issue (COGS) only.
            //         Revenue + cash get booked when payment is recorded later.
            var inventoryAccount = await _db.Accounts.FirstAsync(a => a.Code == "1100");
            var cogsAccount      = await _db.Accounts.FirstAsync(a => a.Code == "5000");
            var arAccount        = await _db.Accounts.FirstAsync(a => a.Code == "1200");
            var revenueAccount   = await _db.Accounts.FirstAsync(a => a.Code == "4000");

            // Receivable booking for COD: DR AR (we're owed money) / CR Revenue.
            //                              DR COGS / CR Inventory.
            var revenue = order.TotalAmount - order.TaxAmount;
            var journalLines = new List<(Guid, decimal, decimal)>
            {
                (arAccount.Id,        order.TotalAmount, 0),
                (revenueAccount.Id,   0, revenue),
                (cogsAccount.Id,      totalCogs, 0),
                (inventoryAccount.Id, 0, totalCogs)
            };
            if (order.TaxAmount > 0)
            {
                var taxAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "2000");
                if (taxAccount != null) journalLines.Add((taxAccount.Id, 0, order.TaxAmount));
            }

            await _accounting.CreateJournalEntryAsync(
                order.Id.ToString(),
                $"Online sale {order.OrderNumber}",
                journalLines);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

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
                ExpectedDelivery = shipping.Eta
            }));
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ============================================================
    //  GET /api/shop/orders/{orderNumber}?phone=
    // ============================================================
    /// <summary>
    /// Guest order tracking. Customer types order number + the phone they used at checkout —
    /// we match both to prevent enumeration. Returns status + fulfillment progress.
    /// </summary>
    [HttpGet("orders/{orderNumber}")]
    public async Task<IActionResult> Track(string orderNumber, [FromQuery] string phone)
    {
        if (string.IsNullOrWhiteSpace(orderNumber) || string.IsNullOrWhiteSpace(phone))
            return BadRequest(ApiResponse<object>.Fail("Order number and phone are required."));

        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber.Trim() && o.GuestPhone == phone.Trim());

        if (order == null)
            return NotFound(ApiResponse<object>.Fail("No order matched that combination."));

        return Ok(ApiResponse<object>.Ok(new
        {
            order.OrderNumber,
            Status            = order.Status.ToString(),
            FulfillmentStatus = order.FulfillmentStatus,
            order.TotalAmount,
            order.ShippingAmount,
            order.ShippingMethodCode,
            order.PaymentMethodCode,
            order.GuestName,
            order.CreatedAt,
            Items = order.Items.Select(i => new { i.SKU, i.Quantity, i.UnitPrice, LineTotal = i.LineTotal })
        }));
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
