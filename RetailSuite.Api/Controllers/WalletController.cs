using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Wallet.Services;
using RetailSuite.Modules.Orders.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Customer-facing wallet endpoints. OTP-based login (phone + 6-digit code),
/// no permanent account needed. Successful verify returns a short-lived JWT
/// with role "WalletCustomer" + customer_id claim — used by the /wallet/me,
/// /wallet/transactions, /wallet/orders endpoints to scope queries to that customer.
///
/// Tenant context comes from the same JWT tenantId claim, so the global query filter
/// keeps everything tenant-scoped.
/// </summary>
[ApiController]
[Route("api/wallet")]
public class WalletController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly IOtpService _otp;
    private readonly IConfiguration _config;
    private readonly ITenantContext _tenantContext;

    public WalletController(
        RetailDbContext db,
        IOtpService otp,
        IConfiguration config,
        ITenantContext tenantContext)
    {
        _db            = db;
        _otp           = otp;
        _config        = config;
        _tenantContext = tenantContext;
    }

    // -------------------------------------------------------------
    // POST /api/wallet/otp/request   { phone }
    // -------------------------------------------------------------
    [HttpPost("otp/request")]
    [AllowAnonymous]
    public async Task<IActionResult> RequestOtp([FromBody] PhoneRequest request)
    {
        var result = await _otp.RequestAsync(request.Phone);
        return Ok(ApiResponse<object>.Ok(new
        {
            sent     = result.Sent,
            message  = result.Message,
            // ONLY in dev-mode delivery — never present when a real SMS provider is wired.
            devOtp   = result.DevOtp
        }));
    }

    // -------------------------------------------------------------
    // POST /api/wallet/otp/verify    { phone, code }
    // -------------------------------------------------------------
    [HttpPost("otp/verify")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyRequest request)
    {
        var result = await _otp.VerifyAsync(request.Phone, request.Code);
        if (result == null)
            return Unauthorized(ApiResponse<object>.Fail("Invalid or expired code."));

        var (customer, tenantId) = result.Value;
        var token = GenerateWalletJwt(customer.Id, tenantId);
        return Ok(ApiResponse<object>.Ok(new
        {
            token,
            customer = new
            {
                customer.Id,
                FullName = customer.FullName,
                customer.Phone,
                customer.Email
            }
        }));
    }

    // -------------------------------------------------------------
    // GET /api/wallet/me        (Bearer wallet JWT)
    // -------------------------------------------------------------
    [HttpGet("me")]
    [Authorize(Policy = "WalletCustomer")]
    public async Task<IActionResult> Me()
    {
        var (customerId, _) = RequireWalletClaims();

        var customer = await _db.Customers
            .Where(c => c.Id == customerId)
            .Select(c => new { c.Id, FullName = c.FullName, c.Phone, c.Email, c.Cnic })
            .FirstOrDefaultAsync();
        if (customer == null)
            return NotFound(ApiResponse<object>.Fail("Customer not found."));

        var storeCreditBalance = await _db.StoreCreditTransactions
            .Where(t => t.CustomerId == customerId)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        var loyaltyBalance = await _db.LoyaltyTransactions
            .Where(t => t.CustomerId == customerId)
            .SumAsync(t => (int?)t.Points) ?? 0;

        // Convert points → rupees using tenant loyalty settings.
        var loyalty = await _db.LoyaltySettings.FirstOrDefaultAsync();
        var pointValueRupees = loyalty?.PointValueRupees ?? 1m;
        var loyaltyRupees = loyaltyBalance * pointValueRupees;

        var orderCount = await _db.Orders.CountAsync(o => o.CustomerId == customerId);

        return Ok(ApiResponse<object>.Ok(new
        {
            customer,
            balances = new
            {
                storeCredit        = storeCreditBalance,
                loyaltyPoints      = loyaltyBalance,
                loyaltyRupeesValue = loyaltyRupees,
                pointValueRupees
            },
            orderCount
        }));
    }

    // -------------------------------------------------------------
    // GET /api/wallet/transactions
    // -------------------------------------------------------------
    [HttpGet("transactions")]
    [Authorize(Policy = "WalletCustomer")]
    public async Task<IActionResult> Transactions()
    {
        var (customerId, _) = RequireWalletClaims();

        var credit = await _db.StoreCreditTransactions
            .Where(t => t.CustomerId == customerId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(50)
            .Select(t => new
            {
                Kind   = "store-credit",
                t.CreatedAt,
                Amount = t.Amount,
                Reason = t.Reason.ToString(),
                t.Note,
                t.OrderId
            })
            .ToListAsync();

        var loyalty = await _db.LoyaltyTransactions
            .Where(t => t.CustomerId == customerId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(50)
            .Select(t => new
            {
                Kind   = "loyalty",
                t.CreatedAt,
                Points = t.Points,
                Reason = t.Reason.ToString(),
                t.Note,
                t.OrderId
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            storeCredit = credit,
            loyalty     = loyalty
        }));
    }

    // -------------------------------------------------------------
    // GET /api/wallet/orders
    // -------------------------------------------------------------
    [HttpGet("orders")]
    [Authorize(Policy = "WalletCustomer")]
    public async Task<IActionResult> Orders()
    {
        var (customerId, _) = RequireWalletClaims();

        var orders = await _db.Orders
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(50)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.InvoiceNumber,
                o.Channel,
                Status            = o.Status.ToString(),
                FulfillmentStatus = o.FulfillmentStatus,
                o.TotalAmount,
                o.PaidAmount,
                o.OutstandingAmount,
                o.CreatedAt
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(orders));
    }

    // ----- helpers ------------------------------------------------------

    private (Guid customerId, Guid tenantId) RequireWalletClaims()
    {
        var customerClaim = User.FindFirst("customer_id")?.Value;
        var tenantClaim   = User.FindFirst("tenantId")?.Value;
        if (!Guid.TryParse(customerClaim, out var customerId) ||
            !Guid.TryParse(tenantClaim,   out var tenantId))
            throw new UnauthorizedAccessException("Wallet claims missing.");
        return (customerId, tenantId);
    }

    private string GenerateWalletJwt(Guid customerId, Guid tenantId)
    {
        var jwtSettings = _config.GetSection("Jwt");
        var claims = new[]
        {
            new Claim("customer_id", customerId.ToString()),
            new Claim("tenantId",    tenantId.ToString()),
            new Claim(ClaimTypes.Role, "WalletCustomer")
        };
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? ""));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer:             jwtSettings["Issuer"],
            audience:           jwtSettings["Audience"],
            claims:             claims,
            expires:            DateTime.UtcNow.AddHours(2),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class PhoneRequest
{
    public string Phone { get; set; } = "";
}

public class VerifyRequest
{
    public string Phone { get; set; } = "";
    public string Code  { get; set; } = "";
}
