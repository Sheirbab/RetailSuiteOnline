using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
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

    public SalesController(RetailDbContext context, SaleService saleService)
    {
        _context = context;
        _saleService = saleService;
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
}
