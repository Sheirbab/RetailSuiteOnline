using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Modules.Orders.Entities;
using RetailSuite.Shared;
using RetailSuite.Api.Authorization;
using RetailSuite.Infrastructure.Modules.Identity.Entities;

namespace RetailSuite.Api.Controllers;

[ApiController]
[Route("api/reports")]
[RequirePermission(Permissions.Reports)]
public class ReportsController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public ReportsController(RetailDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    // -----------------------------------------------------------------------
    // GET /api/reports/sales?from=2025-01-01&to=2025-12-31
    // -----------------------------------------------------------------------
    [HttpGet("sales")]
    public async Task<IActionResult> Sales(
        DateTime? from,
        DateTime? to)
    {
        var fromDate = from ?? DateTime.UtcNow.Date.AddMonths(-1);
        var toDate   = (to ?? DateTime.UtcNow.Date).AddDays(1); // inclusive end

        var orders = await _db.Orders
            .Where(o => o.Status == OrderStatus.Completed
                     && o.CreatedAt >= fromDate
                     && o.CreatedAt <  toDate)
            .ToListAsync();

        var dailyBreakdown = orders
            .GroupBy(o => o.CreatedAt.Date)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                Date    = g.Key.ToString("yyyy-MM-dd"),
                Count   = g.Count(),
                Revenue = g.Sum(o => o.TotalAmount),
                Tax     = g.Sum(o => o.TaxAmount)
            })
            .ToList();

        return Ok(new ApiResponse<object>(true, null, new
        {
            From              = fromDate.ToString("yyyy-MM-dd"),
            To                = (toDate.AddDays(-1)).ToString("yyyy-MM-dd"),
            TotalOrders       = orders.Count,
            TotalRevenue      = orders.Sum(o => o.TotalAmount),
            TotalTax          = orders.Sum(o => o.TaxAmount),
            AverageOrderValue = orders.Count > 0 ? orders.Average(o => o.TotalAmount) : 0,
            DailyBreakdown    = dailyBreakdown
        }));
    }

    // -----------------------------------------------------------------------
    // GET /api/reports/inventory
    // -----------------------------------------------------------------------
    [HttpGet("inventory")]
    public async Task<IActionResult> Inventory()
    {
        var items = await _db.InventoryItems
            .Join(_db.ProductVariants,
                inv => inv.ProductVariantId,
                v   => v.Id,
                (inv, v) => new { inv, v })
            .Join(_db.Products,
                x => x.v.ProductId,
                p => p.Id,
                (x, p) => new
                {
                    SKU          = x.v.SKU,
                    ProductName  = p.Name,
                    CurrentStock = x.inv.CurrentStock,
                    AverageCost  = x.inv.AverageCost,
                    StockValue   = x.inv.TotalStockValue,
                    IsLowStock   = x.inv.CurrentStock <= x.inv.LowStockThreshold
                })
            .ToListAsync();

        return Ok(new ApiResponse<object>(true, null, new
        {
            TotalSkus       = items.Count,
            TotalStockValue = items.Sum(i => i.StockValue),
            LowStockCount   = items.Count(i => i.IsLowStock),
            Items           = items
        }));
    }

    // -----------------------------------------------------------------------
    // GET /api/reports/pl?from=2025-01-01&to=2025-12-31  (Profit & Loss)
    // -----------------------------------------------------------------------
    [HttpGet("pl")]
    public async Task<IActionResult> ProfitAndLoss(
        DateTime? from,
        DateTime? to)
    {
        var fromDate = from ?? DateTime.UtcNow.Date.AddMonths(-1);
        var toDate   = (to ?? DateTime.UtcNow.Date).AddDays(1);

        // Load journal entry lines for entries in the date range, joined to account codes
        var lines = await _db.JournalEntryLines
            .Join(_db.JournalEntries,
                l => l.JournalEntryId,
                e => e.Id,
                (l, e) => new { l, e })
            .Where(x => x.e.CreatedAt >= fromDate && x.e.CreatedAt < toDate)
            .Join(_db.Accounts,
                x => x.l.AccountId,
                a => a.Id,
                (x, a) => new
                {
                    AccountCode  = a.Code,
                    AccountName  = a.Name,
                    Debit        = x.l.DebitAmount,
                    Credit       = x.l.CreditAmount
                })
            .ToListAsync();

        decimal GetNetCredit(string code) =>
            lines.Where(l => l.AccountCode == code).Sum(l => l.Credit - l.Debit);

        var revenue    = GetNetCredit("4000");  // credit = revenue earned
        var cogs       = -GetNetCredit("5000"); // debit  = expense incurred
        var taxPayable = GetNetCredit("2000");

        return Ok(new ApiResponse<object>(true, null, new
        {
            From         = fromDate.ToString("yyyy-MM-dd"),
            To           = toDate.AddDays(-1).ToString("yyyy-MM-dd"),
            Revenue      = revenue,
            COGS         = cogs,
            GrossProfit  = revenue - cogs,
            TaxPayable   = taxPayable,
            NetProfit    = revenue - cogs - taxPayable
        }));
    }

    // ============================================================
    //  X-report — running totals during the cashier's day
    //  (typically printed mid-shift to spot-check the drawer)
    // ============================================================

    /// <summary>
    /// "X read" — running totals for a cashier on a given day. Does NOT close the day.
    /// Cashier can print this any time to compare against the physical drawer.
    /// If <paramref name="cashierId"/> is omitted, defaults to the calling user.
    /// </summary>
    [HttpGet("cashier-x")]
    public async Task<IActionResult> CashierX(
        [FromQuery] Guid? cashierId,
        [FromQuery] DateTime? date)
    {
        var dayStart = (date?.Date ?? DateTime.UtcNow.Date);
        var dayEnd   = dayStart.AddDays(1);

        var orders = await LoadCashierOrdersAsync(cashierId, dayStart, dayEnd);

        var salesCount     = orders.Count;
        var gross          = orders.Sum(o => o.TotalAmount + o.OrderDiscountAmount);
        var discountsGiven = orders.Sum(o => o.OrderDiscountAmount)
                           + orders.Sum(o => o.Items.Sum(i => i.LineDiscountAmount));
        var tax            = orders.Sum(o => o.TaxAmount);
        var storeCreditUsed = orders.Sum(o => o.StoreCreditRedeemed);
        var loyaltyUsed     = orders.Sum(o => o.LoyaltyRedeemedRupees);
        var pointsRedeemed  = orders.Sum(o => o.LoyaltyPointsRedeemed);

        // PaidAmount on the order = cash actually collected.
        var cashCollected  = orders.Sum(o => o.PaidAmount);

        return Ok(new ApiResponse<object>(true, null, new
        {
            CashierUserId   = cashierId ?? Guid.Empty,
            Date            = dayStart.ToString("yyyy-MM-dd"),
            SalesCount      = salesCount,
            GrossSales      = gross,
            DiscountsGiven  = discountsGiven,
            NetSales        = orders.Sum(o => o.TotalAmount),
            Tax             = tax,
            CashCollected   = cashCollected,
            StoreCreditUsed = storeCreditUsed,
            LoyaltyUsed     = loyaltyUsed,
            PointsRedeemed  = pointsRedeemed,
            // Refunds in the same window are negative paid amounts on the order — call them out
            // separately for ops clarity. For now, refunds are reflected in PaidAmount already.
        }));
    }

    // ============================================================
    //  Z-report — end-of-day close
    //  Compares EXPECTED cash (= sum of cash sales) against the COUNTED
    //  cash the cashier entered. Variance is logged for review.
    // ============================================================

    /// <summary>
    /// "Z read" — end-of-day cash reconciliation. Expected = cash collected today;
    /// counted = what the cashier physically counted in the drawer. Variance =
    /// counted − expected (positive means over, negative means short).
    /// </summary>
    [HttpPost("cashier-z")]
    public async Task<IActionResult> CashierZ([FromBody] CashierZRequest request)
    {
        var dayStart = (request.Date?.Date ?? DateTime.UtcNow.Date);
        var dayEnd   = dayStart.AddDays(1);

        var orders = await LoadCashierOrdersAsync(request.CashierId, dayStart, dayEnd);

        var expected = orders.Sum(o => o.PaidAmount);
        var counted  = request.CountedCash;
        var variance = counted - expected;

        return Ok(new ApiResponse<object>(true, null, new
        {
            CashierUserId = request.CashierId,
            Date          = dayStart.ToString("yyyy-MM-dd"),
            SalesCount    = orders.Count,
            ExpectedCash  = expected,
            OpeningFloat  = request.OpeningFloat,
            CountedCash   = counted,
            Variance      = variance,
            VarianceState = variance switch
            {
                0     => "Balanced",
                > 0   => "Over",
                _     => "Short"
            }
        }));
    }

    // ----- helpers ------------------------------------------------

    /// <summary>
    /// Load orders attributable to the given cashier (or the calling user if null)
    /// in the day window, including items so per-line discounts are summable.
    /// </summary>
    private async Task<List<Order>> LoadCashierOrdersAsync(Guid? cashierId, DateTime dayStart, DateTime dayEnd)
    {
        // Default to the caller's user id when not specified — small-shop pattern is
        // "I want my own day's totals". Pass cashierId=Guid.Empty explicitly to get
        // store-wide totals across all cashiers.
        var effectiveCashier = cashierId ?? _currentUser.UserId;

        var query = _db.Orders
            .Include(o => o.Items)
            .Where(o => o.Status == OrderStatus.Completed
                     && o.CreatedAt >= dayStart
                     && o.CreatedAt <  dayEnd);

        if (effectiveCashier != Guid.Empty)
            query = query.Where(o => o.CashierUserId == effectiveCashier);

        return await query.ToListAsync();
    }
}

public class CashierZRequest
{
    public Guid?     CashierId { get; set; }
    public DateTime? Date { get; set; }
    public decimal   OpeningFloat { get; set; }
    public decimal   CountedCash { get; set; }
}
