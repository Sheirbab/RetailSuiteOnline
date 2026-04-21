using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Orders.Dtos;
using RetailSuite.Infrastructure.Modules.Orders.Services;
namespace RetailSuite.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SalesController : ControllerBase
    {
        private readonly RetailDbContext _context;
        private readonly SaleService _saleService;
        public SalesController(RetailDbContext context, SaleService saleService)
        {
            _context = context;
            _saleService = saleService;
        }
        [HttpPost("checkout")]
        public async Task<IActionResult> Checkout([FromBody] CreatePosSaleRequest request)
        {
            var orderId = await _saleService.ProcessPosSaleAsync(request);
            return Ok(orderId);
        }
    }
}
