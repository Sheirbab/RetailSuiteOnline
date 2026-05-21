using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Customer.Entities;
using RetailSuite.Infrastructure.Modules.Customer.Services;
using RetailSuite.Modules.Orders.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Customer ledgers — store credit, loyalty points, purchase history.
/// Staff/Admin only; loyalty config endpoints are Admin-only.
/// </summary>
[ApiController]
[Route("api/customers/{customerId:guid}")]
[Authorize(Policy = "StaffOrAdmin")]
public class CustomerLedgersController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly IStoreCreditService _credit;
    private readonly ILoyaltyService _loyalty;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUser;

    public CustomerLedgersController(
        RetailDbContext db,
        IStoreCreditService credit,
        ILoyaltyService loyalty,
        ITenantContext tenantContext,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _credit = credit;
        _loyalty = loyalty;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    // ============================================================
    //  Store credit
    // ============================================================

    [HttpGet("store-credit")]
    public async Task<IActionResult> GetStoreCredit(Guid customerId)
    {
        var tenantId = RequireTenantId();
        var balance = await _credit.GetBalanceAsync(tenantId, customerId);
        var history = await _credit.GetHistoryAsync(tenantId, customerId);
        return Ok(ApiResponse<object>.Ok(new
        {
            Balance = balance,
            History = history.Select(t => new
            {
                t.Id, t.Amount, t.Currency,
                Reason = t.Reason.ToString(),
                t.Note, t.OrderId, t.CreatedAt
            })
        }));
    }

    [HttpPost("store-credit/issue")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> IssueStoreCredit(Guid customerId, [FromBody] IssueCreditRequest request)
    {
        if (request.Amount <= 0)
            return BadRequest(ApiResponse<object>.Fail("Amount must be > 0."));

        var entry = await _credit.IssueAsync(
            RequireTenantId(),
            customerId,
            request.Amount,
            string.IsNullOrWhiteSpace(request.Reason) ? StoreCreditReason.Goodwill
                : (StoreCreditReason)Enum.Parse(typeof(StoreCreditReason), request.Reason, ignoreCase: true),
            request.Note,
            orderId: null,
            createdByUserId: _currentUser.UserId);

        return Ok(ApiResponse<object>.Ok(new { entry.Id, entry.Amount, Reason = entry.Reason.ToString() }));
    }

    // ============================================================
    //  Loyalty
    // ============================================================

    [HttpGet("loyalty")]
    public async Task<IActionResult> GetLoyalty(Guid customerId)
    {
        var tenantId = RequireTenantId();
        var balance  = await _loyalty.GetBalanceAsync(tenantId, customerId);
        var history  = await _loyalty.GetHistoryAsync(tenantId, customerId);
        var settings = await _loyalty.GetSettingsAsync(tenantId);
        return Ok(ApiResponse<object>.Ok(new
        {
            Balance  = balance,
            Settings = new
            {
                settings.RupeesPerPoint,
                settings.MinRedeemPoints,
                settings.PointValueRupees,
                settings.MaxRedemptionPercentOfOrder,
                settings.IsEnabled
            },
            History = history.Select(t => new
            {
                t.Id, t.Points,
                Reason = t.Reason.ToString(),
                t.OrderId, t.RupeesValue, t.Note, t.CreatedAt
            })
        }));
    }

    // ============================================================
    //  Purchase history (read-only)
    // ============================================================

    [HttpGet("orders")]
    public async Task<IActionResult> GetPurchaseHistory(Guid customerId, int take = 50)
    {
        var rows = await _db.Orders
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(take)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                Status = o.Status.ToString(),
                o.TotalAmount,
                o.TaxAmount,
                o.PaidAmount,
                o.CreatedAt
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(rows));
    }

    private Guid RequireTenantId() =>
        _tenantContext.TenantId
        ?? throw new UnauthorizedAccessException("Tenant context missing.");
}

public class IssueCreditRequest
{
    public decimal Amount { get; set; }
    public string? Reason { get; set; }    // StoreCreditReason name; default Goodwill
    public string? Note   { get; set; }
}
