using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Payment.Dtos;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly PaymentService _paymentService;
    private readonly RetailDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public PaymentsController(
        PaymentService paymentService,
        RetailDbContext db,
        ICurrentUserContext currentUser)
    {
        _paymentService = paymentService;
        _db = db;
        _currentUser = currentUser;
    }
    [HttpPost]
    public async Task<IActionResult> Receive(ReceivePaymentRequest request)
    {
        var order = await _db.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId);

        if (order == null)
            return NotFound();

        // If customer, ensure ownership
        if (_currentUser.Role == "Customer")
        {
            var customer = await _db.Customers
                .FirstOrDefaultAsync(c => c.UserId == _currentUser.UserId);

            if (customer == null || order.CustomerId != customer.Id)
                return Forbid();
        }

        await _paymentService.ReceivePaymentAsync(
            request.OrderId,
            request.Amount,
            request.Method);

        return Ok();
    }
    [Authorize(Policy = "StaffOrAdmin")]
    [HttpPost("{paymentId}/reverse")]
    public async Task<IActionResult> Reverse(Guid paymentId)
    {
        await _paymentService.DeletePaymentAsync(paymentId);
        return Ok();
    }
    [HttpGet("order/{orderId}")]
    public async Task<IActionResult> GetByOrder(Guid orderId)
    {
        if (_currentUser.Role == "Customer")
        {
            var customer = await _db.Customers
                .FirstOrDefaultAsync(c => c.UserId == _currentUser.UserId);

            if (customer == null)
                return Forbid();

            var isOwner = await _db.Orders.AnyAsync(o => o.Id == orderId && o.CustomerId == customer.Id);
            if (!isOwner)
                return Forbid();
        }

        var payments = await _db.Payments
            .Where(p => p.OrderId == orderId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return Ok(payments);
    }
    [HttpGet("order/{orderId}/outstanding")]
    public async Task<IActionResult> GetOutstanding(Guid orderId)
    {
        // For customers, check authorization
        if (_currentUser.Role == "Customer")
        {
            var customer = await _db.Customers
                .FirstOrDefaultAsync(c => c.UserId == _currentUser.UserId);

            if (customer == null)
                return Forbid();

            var order = await _db.Orders
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                return NotFound();

            // Check if customer owns this order
            if (order.CustomerId != customer.Id)
                return Forbid(); // Resource exists but user doesn't have access

            return Ok(new
            {
                order.TotalAmount,
                order.PaidAmount,
                order.OutstandingAmount
            });
        }

        // Staff/Admin can see any order
        var adminOrder = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (adminOrder == null)
            return NotFound();

        return Ok(new
        {
            adminOrder.TotalAmount,
            adminOrder.PaidAmount,
            adminOrder.OutstandingAmount
        });
    }
    [Authorize(Policy = "AdminOnly")]
    [HttpGet]
    public async Task<IActionResult> List(DateTime? from, DateTime? to, int page = 1, int pageSize = 20)
    {
        var query = _db.Payments.AsQueryable();

        if (from.HasValue)
            query = query.Where(p => p.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(p => p.CreatedAt <= to.Value);

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = items
        });
    }
}
