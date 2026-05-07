using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Orders.Dtos;
using RetailSuite.Infrastructure.Modules.Orders.Services;
using RetailSuite.Modules.Orders.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers
{
    [ApiController]
    [Route("api/orders")]
    [Authorize]
    public class OrdersController : ControllerBase
    {
        private readonly OrderService _orderService;
        private readonly RetailDbContext _db;
        private readonly ICurrentUserContext _currentUser;

        public OrdersController(
            OrderService orderService,
            RetailDbContext db,
            ICurrentUserContext currentUser)
        {
            _orderService = orderService;
            _db = db;
            _currentUser = currentUser;
        }
        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid id)
        {
            // For customers, check authorization first (before querying for the resource)
            if (_currentUser.Role == "Customer")
            {
                var customer = await _db.Customers
                    .FirstOrDefaultAsync(c => c.UserId == _currentUser.UserId);

                // If no customer profile, access is forbidden
                if (customer == null)
                    return Forbid();

                // Customer can only access their own orders
                var order = await _db.Orders
                    .Include(o => o.Items)
                    .Include(o => o.Payments)
                    .FirstOrDefaultAsync(o => o.Id == id);

                if (order == null)
                    return NotFound();

                // Check if customer owns this order
                if (order.CustomerId != customer.Id)
                    return Forbid(); // Resource exists but user doesn't have access

                return Ok(new
                {
                    order.Id,
                    order.OrderNumber,
                    order.Status,
                    order.TotalAmount,
                    order.PaidAmount,
                    order.OutstandingAmount,
                    Items = order.Items,
                    Payments = order.Payments
                });
            }

            // Staff/Admin can see any order
            var adminOrder = await _db.Orders
                .Include(o => o.Items)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (adminOrder == null)
                return NotFound();

            return Ok(new
            {
                adminOrder.Id,
                adminOrder.OrderNumber,
                adminOrder.Status,
                adminOrder.TotalAmount,
                adminOrder.PaidAmount,
                adminOrder.OutstandingAmount,
                Items = adminOrder.Items,
                Payments = adminOrder.Payments
            });
        }
        [Authorize(Policy = "StaffOrAdmin")]
        [HttpGet]
        public async Task<IActionResult> List(
                                                OrderStatus? status,
                                                DateTime? from,
                                                DateTime? to,
                                                int page = 1,
                                                int pageSize = 20)
        {
            var query = _db.Orders.AsQueryable();

            if (status.HasValue)
                query = query.Where(o => o.Status == status.Value);

            if (from.HasValue)
                query = query.Where(o => o.CreatedAt >= from.Value);

            if (to.HasValue)
                query = query.Where(o => o.CreatedAt <= to.Value);

            var total = await query.CountAsync();

            var items = await query
                .OrderByDescending(o => o.CreatedAt)
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
        [Authorize(Policy = "CustomerOnly")]
        [HttpGet("my")]
        public async Task<IActionResult> MyOrders()
        {
            var userId = _currentUser.UserId;

            var customer = await _db.Customers
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (customer == null)
                return BadRequest("Customer profile not found.");

            var orders = await _db.Orders
                .Where(o => o.CustomerId == customer.Id)
                .ToListAsync();

            return Ok(orders);
        }
        [Authorize(Policy = "StaffOrAdmin")]
        [HttpPost("{id}/confirm")]
        public async Task<IActionResult> Confirm(Guid id)
        {
            await _orderService.ConfirmOrderAsync(id);
            return Ok();
        }
        [Authorize(Policy = "StaffOrAdmin")]
        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> Cancel(Guid id)
        {
            await _orderService.CancelOrderAsync(id);
            return Ok();
        }
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, CreateOrderRequest request)
        {
            // For customers, check authorization
            if (_currentUser.Role == "Customer")
            {
                var customer = await _db.Customers
                    .FirstOrDefaultAsync(c => c.UserId == _currentUser.UserId);

                if (customer == null)
                    return Forbid();

                var order = await _db.Orders
                    .FirstOrDefaultAsync(o => o.Id == id);

                if (order == null)
                    return NotFound();

                // Check if customer owns this order
                if (order.CustomerId != customer.Id)
                    return Forbid(); // Resource exists but user doesn't have access
            }

            await _orderService.UpdateDraftAsync(id, request);
            return Ok();
        }
        [Authorize(Policy = "CustomerOnly")]
        [HttpPost]
        public async Task<IActionResult> Create(CreateOrderRequest request)
        {
            var orderId = await _orderService.CreateDraftAsync(request);
            return Ok(orderId);
        }

        /// <summary>Process a return/refund for a completed or confirmed order.</summary>
        [Authorize(Policy = "StaffOrAdmin")]
        [HttpPost("{id}/return")]
        public async Task<IActionResult> Return(Guid id, [FromBody] ReturnOrderRequest request)
        {
            var refundAmount = await _orderService.ProcessReturnAsync(id, request);
            return Ok(new ApiResponse<object>(true, "Return processed successfully.", new
            {
                OrderId      = id,
                RefundAmount = refundAmount
            }));
        }
    }
}
