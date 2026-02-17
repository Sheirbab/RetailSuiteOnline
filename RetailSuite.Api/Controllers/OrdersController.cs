using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RetailSuite.Infrastructure.Modules.Orders.Services;

namespace RetailSuite.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController(OrderService orderService) : ControllerBase
    {
        public OrderService _orderService { get; } = orderService;
        [HttpPost("{orderId}/confirm")]
        public async Task<IActionResult> Confirm(Guid orderId)
        {
            await _orderService.ConfirmOrderAsync(orderId);
            return Ok();
        }
        [HttpPost("{orderId}/cancel")]
        public async Task<IActionResult> Cancel(Guid orderId)
        {
            await _orderService.CancelOrderAsync(orderId);
            return Ok();
        }
    }
}
