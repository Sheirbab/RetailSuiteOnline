using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RetailSuite.Modules.Orders.Services;

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
    }
}
