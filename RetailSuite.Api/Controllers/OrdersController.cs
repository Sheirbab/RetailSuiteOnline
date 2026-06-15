using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Orders.Dtos;
using RetailSuite.Infrastructure.Modules.Orders.Services;
using RetailSuite.Infrastructure.Modules.Subscriptions.Services;
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
        private readonly IEntitlementService _entitlements;
        private readonly ILogger<OrdersController> _logger;

        public OrdersController(
            OrderService orderService,
            RetailDbContext db,
            ICurrentUserContext currentUser,
            IEntitlementService entitlements,
            ILogger<OrdersController> logger)
        {
            _orderService = orderService;
            _db = db;
            _currentUser = currentUser;
            _entitlements = entitlements;
            _logger = logger;
        }
        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid id)
        {
            _logger.LogInformation("Fetching order {OrderId} by {UserRole} {UserId}", id, _currentUser.Role, _currentUser.UserId);

            // For customers, check authorization first (before querying for the resource)
            if (_currentUser.Role == "Customer")
            {
                var customer = await _db.Customers
                    .FirstOrDefaultAsync(c => c.UserId == _currentUser.UserId);

                // If no customer profile, access is forbidden
                if (customer == null)
                {
                    _logger.LogWarning("Order access denied: Customer profile not found for UserId {UserId}", _currentUser.UserId);
                    return Forbid();
                }

                // Customer can only access their own orders
                var order = await _db.Orders
                    .Include(o => o.Items)
                    .Include(o => o.Payments)
                    .FirstOrDefaultAsync(o => o.Id == id);

                if (order == null)
                {
                    _logger.LogInformation("Order {OrderId} not found", id);
                    return NotFound();
                }

                // Check if customer owns this order
                if (order.CustomerId != customer.Id)
                {
                    _logger.LogWarning("Order access denied: CustomerId {CustomerId} attempting to access OrderId {OrderId}", customer.Id, id);
                    return Forbid(); // Resource exists but user doesn't have access
                }

                _logger.LogInformation("Order {OrderId} retrieved successfully for customer {CustomerId}", id, customer.Id);

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
            // Plan-limit enforcement — MaxOrdersPerMonth on the active plan.
            var quota = await _entitlements.CanCreateOrderAsync(_currentUser.TenantId);
            if (!quota.Allowed)
            {
                return StatusCode(StatusCodes.Status402PaymentRequired,
                    new ApiResponse<object>(false, quota.Reason, new
                    {
                        quota.CurrentCount,
                        quota.Limit
                    }));
            }

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
