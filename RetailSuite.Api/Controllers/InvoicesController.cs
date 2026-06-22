using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Modules.Orders.Entities;
using RetailSuite.Shared;
using RetailSuite.Api.Authorization;
using RetailSuite.Infrastructure.Modules.Identity.Entities;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Read-only endpoints around FBR-compliant invoices.
///   GET /{orderId} — printable JSON for one invoice (seller + buyer + lines + totals)
///   GET /export     — CSV of all invoices in a date window, for FBR filing
///
/// Authoring (issue the invoice number) happens automatically in the sale flow —
/// see <see cref="Infrastructure.Modules.Tax.Services.IInvoiceStampingService"/>.
/// </summary>
[ApiController]
[Route("api/invoices")]
[RequirePermission(Permissions.Reports)]
public class InvoicesController : ControllerBase
{
    private readonly RetailDbContext _db;
    public InvoicesController(RetailDbContext db) => _db = db;

    // -------------------------------------------------------------
    // GET /api/invoices/{orderId}
    // -------------------------------------------------------------
    [HttpGet("{orderId:guid}")]
    public async Task<IActionResult> Get(Guid orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
            return NotFound(ApiResponse<object>.Fail("Order not found."));
        if (string.IsNullOrEmpty(order.InvoiceNumber))
            return BadRequest(ApiResponse<object>.Fail(
                "Order has no invoice — only Confirmed/Completed orders carry an invoice number."));

        var customer = order.CustomerId == Guid.Empty
            ? null
            : await _db.Customers
                .Where(c => c.Id == order.CustomerId)
                .Select(c => new { c.Id, c.FullName, c.Phone, c.Email, c.Cnic })
                .FirstOrDefaultAsync();

        // Per-tax-rate breakdown (e.g. lines at 0%, 18%, ...)
        var taxBreakdown = order.Items
            .GroupBy(i => i.TaxRate)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                Rate     = g.Key,
                RatePct  = g.Key * 100m,
                Taxable  = g.Sum(i => i.LineNet),
                TaxValue = g.Sum(i => i.LineTaxAmount)
            })
            .ToList();

        return Ok(ApiResponse<object>.Ok(new
        {
            Seller = new
            {
                Ntn          = order.SellerNtnSnapshot,
                Strn         = order.SellerStrnSnapshot,
                BusinessName = order.SellerBusinessNameSnapshot,
                Address      = order.SellerAddressSnapshot
            },
            Invoice = new
            {
                Number     = order.InvoiceNumber,
                IssuedAt   = order.InvoiceIssuedAt,
                Channel    = order.Channel,
                FbrNumber  = order.FbrInvoiceNumber,
                OrderNumber = order.OrderNumber
            },
            Buyer = customer ?? (object?)new
            {
                FullName = order.GuestName,
                Phone    = order.GuestPhone,
                Email    = order.GuestEmail
            },
            Items = order.Items.Select(i => new
            {
                Sku        = i.SKU,
                i.UnitPrice,
                i.Quantity,
                i.LineDiscountAmount,
                LineNet      = i.LineNet,
                TaxRate      = i.TaxRate,
                TaxRatePct   = i.TaxRate * 100m,
                LineTaxValue = i.LineTaxAmount,
                LineTotal    = i.LineTotal
            }),
            Totals = new
            {
                SubtotalNet        = order.Items.Sum(i => i.LineNet),
                TaxBreakdown       = taxBreakdown,
                TaxTotal           = order.TaxAmount,
                OrderDiscount      = order.OrderDiscountAmount,
                ShippingAmount     = order.ShippingAmount,
                StoreCreditUsed    = order.StoreCreditRedeemed,
                LoyaltyRupeesUsed  = order.LoyaltyRedeemedRupees,
                GrandTotal         = order.TotalAmount,
                PaidAmount         = order.PaidAmount,
                Outstanding        = order.OutstandingAmount
            }
        }));
    }

    // -------------------------------------------------------------
    // GET /api/invoices/export?from=2025-07-01&to=2026-06-30
    // Returns CSV suitable for FBR sales-tax filing.
    // -------------------------------------------------------------
    [HttpGet("export")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Export([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var fromDate = from ?? DateTime.UtcNow.Date.AddMonths(-1);
        var toDate   = (to ?? DateTime.UtcNow.Date).AddDays(1);

        var orders = await _db.Orders
            .Include(o => o.Items)
            .Where(o => o.InvoiceNumber != null
                     && o.InvoiceIssuedAt >= fromDate
                     && o.InvoiceIssuedAt <  toDate)
            .OrderBy(o => o.InvoiceIssuedAt)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("InvoiceNumber,IssuedAt,Channel,BuyerName,BuyerCnic,SubtotalNet,TaxAmount,GrandTotal,SellerNtn,SellerStrn");

        foreach (var o in orders)
        {
            var buyerName = !string.IsNullOrEmpty(o.GuestName)
                ? o.GuestName
                : await _db.Customers.Where(c => c.Id == o.CustomerId).Select(c => c.FullName).FirstOrDefaultAsync();
            var buyerCnic = await _db.Customers.Where(c => c.Id == o.CustomerId).Select(c => c.Cnic).FirstOrDefaultAsync();

            var subtotalNet = o.Items.Sum(i => i.LineNet);
            sb.Append(Csv(o.InvoiceNumber!)).Append(',')
              .Append(o.InvoiceIssuedAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(',')
              .Append(Csv(o.Channel)).Append(',')
              .Append(Csv(buyerName ?? "")).Append(',')
              .Append(Csv(buyerCnic ?? "")).Append(',')
              .Append(subtotalNet.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(o.TaxAmount.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(o.TotalAmount.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(Csv(o.SellerNtnSnapshot ?? "")).Append(',')
              .Append(Csv(o.SellerStrnSnapshot ?? ""))
              .AppendLine();
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var filename = $"invoices_{fromDate:yyyyMMdd}_{toDate.AddDays(-1):yyyyMMdd}.csv";
        return File(bytes, "text/csv", filename);
    }

    private static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        var escaped = value.Replace("\"", "\"\"");
        return needsQuotes ? $"\"{escaped}\"" : escaped;
    }
}
