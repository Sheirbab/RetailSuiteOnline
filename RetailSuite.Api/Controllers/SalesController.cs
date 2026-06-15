using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Customer.Entities;
using RetailSuite.Infrastructure.Modules.Customer.Services;
using RetailSuite.Infrastructure.Modules.Orders.Dtos;
using RetailSuite.Infrastructure.Modules.Orders.Services;
using RetailSuite.Modules.Orders.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

[ApiController]
[Route("api/sales")]
[Authorize(Policy = "StaffOrAdmin")]
public class SalesController : ControllerBase
{
    private readonly RetailDbContext _context;
    private readonly SaleService _saleService;
    private readonly IStoreCreditService _storeCredit;
    private readonly ILoyaltyService _loyalty;
    private readonly ICurrentUserContext _currentUser;
    private readonly ITenantContext _tenantContext;

    public SalesController(
        RetailDbContext context,
        SaleService saleService,
        IStoreCreditService storeCredit,
        ILoyaltyService loyalty,
        ICurrentUserContext currentUser,
        ITenantContext tenantContext)
    {
        _context = context;
        _saleService = saleService;
        _storeCredit = storeCredit;
        _loyalty = loyalty;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// POS Checkout — creates and completes a sale in one atomic step.
    /// </summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CreatePosSaleRequest request)
    {
        if (request.Items == null || request.Items.Count == 0)
            return BadRequest(ApiResponse<object>.Fail("Cart is empty."));

        var orderId = await _saleService.ProcessPosSaleAsync(request);

        return Ok(new ApiResponse<Guid>(true, "Sale completed successfully.", orderId));
    }

    /// <summary>
    /// Today's sales summary — count of completed orders and total revenue for the dashboard.
    /// </summary>
    [HttpGet("today")]
    public async Task<IActionResult> Today()
    {
        var startOfDay = DateTime.UtcNow.Date;
        var endOfDay   = startOfDay.AddDays(1);

        var orders = await _context.Orders
            .Where(o =>
                o.Status == OrderStatus.Completed &&
                o.CreatedAt >= startOfDay &&
                o.CreatedAt < endOfDay)
            .ToListAsync();

        return Ok(new ApiResponse<object>(true, null, new
        {
            SalesCount   = orders.Count,
            TotalRevenue = orders.Sum(o => o.TotalAmount),
            Date         = startOfDay.ToString("yyyy-MM-dd")
        }));
    }

    // ============================================================
    //  Customer lookup at the POS counter
    // ============================================================

    /// <summary>
    /// Quick customer lookup by phone / CNIC. POS cashier types a number and
    /// the matching customer (if any) is attached to the in-progress sale.
    /// Returns store-credit and loyalty balance so the cashier can quote them
    /// to the customer ("you have Rs 500 store credit, want to use it?").
    /// </summary>
    [HttpGet("customer-lookup")]
    public async Task<IActionResult> CustomerLookup(
        [FromQuery] string? phone,
        [FromQuery] string? cnic)
    {
        if (string.IsNullOrWhiteSpace(phone) && string.IsNullOrWhiteSpace(cnic))
            return BadRequest(ApiResponse<object>.Fail("Provide phone or cnic."));

        var query = _context.Customers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(phone)) query = query.Where(c => c.Phone == phone.Trim());
        if (!string.IsNullOrWhiteSpace(cnic))  query = query.Where(c => c.Cnic  == cnic.Trim());

        var customer = await query.FirstOrDefaultAsync();
        if (customer == null)
            return Ok(new ApiResponse<object>(true, "No match", null));

        var tenantId    = RequireTenantId();
        var credit      = await _storeCredit.GetBalanceAsync(tenantId, customer.Id);
        var points      = await _loyalty.GetBalanceAsync(tenantId, customer.Id);
        var settings    = await _loyalty.GetSettingsAsync(tenantId);

        return Ok(new ApiResponse<object>(true, null, new
        {
            customer.Id,
            customer.FullName,
            customer.Email,
            customer.Phone,
            customer.Cnic,
            Group        = customer.Group.ToString(),
            StoreCredit  = credit,
            LoyaltyPoints = points,
            LoyaltyRupeesValue = points * settings.PointValueRupees
        }));
    }

    // ============================================================
    //  POST /api/sales/customer-quick-add
    // ============================================================
    /// <summary>
    /// Lightweight customer creation from the POS — just name + phone (no auth account).
    /// Used when a lookup misses and the cashier wants to register the walk-in
    /// customer right at the till without leaving the sale flow. Returns the same shape
    /// as customer-lookup so the POS can attach it immediately.
    /// </summary>
    [HttpPost("customer-quick-add")]
    public async Task<IActionResult> CustomerQuickAdd([FromBody] QuickCustomerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Phone))
            return BadRequest(ApiResponse<object>.Fail("Name and phone are required."));

        var phone = request.Phone.Trim();
        var existing = await _context.Customers.FirstOrDefaultAsync(c => c.Phone == phone);
        if (existing != null)
            return Conflict(ApiResponse<object>.Fail("A customer with this phone already exists."));

        // Split "Ali Khan" → "Ali" / "Khan"; single-word names go to FirstName.
        var name = request.Name.Trim();
        var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts[0];
        var lastName  = parts.Length > 1 ? parts[1] : "";

        // Walk-in customers have no User account — UserId = Guid.Empty.
        var customer = new Customer(Guid.Empty, firstName, lastName, email: null, phone: phone);
        if (!string.IsNullOrWhiteSpace(request.Cnic)) customer.SetCnic(request.Cnic.Trim());

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var tenantId = RequireTenantId();
        var settings = await _loyalty.GetSettingsAsync(tenantId);

        return Ok(new ApiResponse<object>(true, "Customer added.", new
        {
            customer.Id,
            customer.FullName,
            customer.Email,
            customer.Phone,
            customer.Cnic,
            Group              = customer.Group.ToString(),
            StoreCredit        = 0m,
            LoyaltyPoints      = 0,
            LoyaltyRupeesValue = 0m * settings.PointValueRupees
        }));
    }

    // ============================================================
    //  Held sales (parked carts)
    // ============================================================

    /// <summary>
    /// Park the current cart so the cashier can serve another customer. Returns the held-sale id —
    /// pass it back as <c>resumedFromHeldSaleId</c> when the customer returns.
    /// </summary>
    [HttpPost("hold")]
    public async Task<IActionResult> Hold([FromBody] HoldSaleRequest request)
    {
        if (request.Items == null || request.Items.Count == 0)
            return BadRequest(ApiResponse<object>.Fail("Cart is empty."));

        var tenantId  = RequireTenantId();
        var cashierId = _currentUser.UserId;

        // Snapshot the current cart as HeldCartLine records.
        var lines = request.Items.Select(i => new HeldCartLine(
            VariantId:          i.ProductVariantId,
            Sku:                i.Sku ?? string.Empty,
            Quantity:           i.Quantity,
            UnitPrice:          i.UnitPrice,
            LineDiscountAmount: i.LineDiscountAmount,
            TaxRate:            i.TaxRate)).ToList();

        var held = new HeldSale(
            tenantId, cashierId,
            request.Label ?? $"Held @ {DateTime.UtcNow:HH:mm}",
            request.CustomerId,
            request.CustomerPhone,
            lines,
            request.OrderDiscountAmount,
            request.Notes);

        _context.HeldSales.Add(held);
        await _context.SaveChangesAsync();

        return Ok(new ApiResponse<Guid>(true, "Sale held.", held.Id));
    }

    /// <summary>List the cashier's current held sales (newest first).</summary>
    [HttpGet("held")]
    public async Task<IActionResult> ListHeld([FromQuery] bool mineOnly = true)
    {
        var cashierId = _currentUser.UserId;
        var query = _context.HeldSales.AsQueryable();
        if (mineOnly) query = query.Where(h => h.CashierUserId == cashierId);

        var rows = await query
            .OrderByDescending(h => h.CreatedAt)
            .Take(50)
            .Select(h => new
            {
                h.Id,
                h.Label,
                h.CustomerId,
                h.CustomerPhone,
                h.CashierUserId,
                h.OrderDiscountAmount,
                h.Notes,
                h.CreatedAt
            })
            .ToListAsync();

        return Ok(new ApiResponse<object>(true, null, rows));
    }

    /// <summary>Get a held sale's full snapshot — used by the POS to rehydrate the cart.</summary>
    [HttpGet("held/{id:guid}")]
    public async Task<IActionResult> GetHeld(Guid id)
    {
        var held = await _context.HeldSales.FirstOrDefaultAsync(h => h.Id == id);
        if (held == null)
            return NotFound(ApiResponse<object>.Fail("Held sale not found."));

        return Ok(new ApiResponse<object>(true, null, new
        {
            held.Id,
            held.Label,
            held.CustomerId,
            held.CustomerPhone,
            held.OrderDiscountAmount,
            held.Notes,
            Items = held.GetLines()
        }));
    }

    /// <summary>Discard a held sale without completing it. Stock was never reserved so this is just a cleanup.</summary>
    [HttpDelete("held/{id:guid}")]
    public async Task<IActionResult> DiscardHeld(Guid id)
    {
        var held = await _context.HeldSales.FirstOrDefaultAsync(h => h.Id == id);
        if (held == null)
            return NotFound(ApiResponse<object>.Fail("Held sale not found."));

        _context.HeldSales.Remove(held);
        await _context.SaveChangesAsync();
        return Ok(new ApiResponse<object>(true, "Discarded.", new { id }));
    }

    // ============================================================
    //  Return-from-POS: lookup a previous receipt and refund it
    // ============================================================

    /// <summary>
    /// Find a recent sale by receipt / order number or customer phone.
    /// Used by the cashier when a customer brings goods back — they pick the
    /// receipt from the result list then process the return via the existing
    /// /api/orders/{id}/return endpoint.
    /// </summary>
    [HttpGet("return-lookup")]
    public async Task<IActionResult> ReturnLookup(
        [FromQuery] string? orderNumber,
        [FromQuery] string? phone)
    {
        if (string.IsNullOrWhiteSpace(orderNumber) && string.IsNullOrWhiteSpace(phone))
            return BadRequest(ApiResponse<object>.Fail("Provide orderNumber or phone."));

        var query = _context.Orders
            .Include(o => o.Items)
            .Include(o => o.Customer)
            .Where(o => o.Status == OrderStatus.Completed || o.Status == OrderStatus.Confirmed);

        if (!string.IsNullOrWhiteSpace(orderNumber))
            query = query.Where(o => o.OrderNumber.Contains(orderNumber));
        if (!string.IsNullOrWhiteSpace(phone))
            query = query.Where(o => o.Customer != null && o.Customer.Phone == phone.Trim());

        var rows = await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(15)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                Status     = o.Status.ToString(),
                o.TotalAmount,
                o.PaidAmount,
                o.CreatedAt,
                CustomerName = o.Customer != null ? o.Customer.FullName : null,
                Items = o.Items.Select(i => new
                {
                    i.Id,
                    i.ProductVariantId,
                    i.SKU,
                    i.Quantity,
                    i.UnitPrice,
                    i.LineDiscountAmount,
                    LineTotal = i.LineTotal
                }).ToList()
            })
            .ToListAsync();

        return Ok(new ApiResponse<object>(true, null, rows));
    }

    private Guid RequireTenantId() =>
        _tenantContext.TenantId
        ?? throw new UnauthorizedAccessException("Tenant context missing.");
}

// ----- DTOs ----------------------------------

public class QuickCustomerRequest
{
    public string Name  { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Cnic { get; set; }
}

public class HoldSaleRequest
{
    public string? Label { get; set; }
    public Guid?   CustomerId { get; set; }
    public string? CustomerPhone { get; set; }
    public decimal OrderDiscountAmount { get; set; }
    public string? Notes { get; set; }
    public List<HoldSaleLine> Items { get; set; } = new();
}

public class HoldSaleLine
{
    public Guid    ProductVariantId { get; set; }
    public string? Sku { get; set; }
    public int     Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineDiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
}
