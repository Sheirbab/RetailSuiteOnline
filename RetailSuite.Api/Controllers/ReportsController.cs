using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Modules.Orders.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Policy = "StaffOrAdmin")]
public class ReportsController : ControllerBase
{
    private readonly RetailDbContext _db;

    public ReportsController(RetailDbContext db)
    {
        _db = db;
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
}
