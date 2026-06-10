using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Subscriptions.Dtos;
using RetailSuite.Infrastructure.Modules.Subscriptions.Entities;
using RetailSuite.Infrastructure.Modules.Subscriptions.Services;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

[ApiController]
[Route("api/subscriptions")]
public class SubscriptionsController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly ISubscriptionService _subs;
    private readonly ITenantContext _tenantContext;

    public SubscriptionsController(
        RetailDbContext db,
        ISubscriptionService subs,
        ITenantContext tenantContext)
    {
        _db = db;
        _subs = subs;
        _tenantContext = tenantContext;
    }

    // -------------------------------------------------------------
    // GET /api/subscriptions/plans  — public catalog
    // -------------------------------------------------------------
    [HttpGet("plans")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPlans()
    {
        var plans = await _db.SubscriptionPlans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.MonthlyPrice)
            .Select(p => p.ToResponse())
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(plans));
    }

    // -------------------------------------------------------------
    // GET /api/subscriptions/me  — current tenant's subscription
    // -------------------------------------------------------------
    [HttpGet("me")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetMine()
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");

        var sub = await _subs.GetActiveAsync(tenantId);
        if (sub == null)
            return NotFound(ApiResponse<object>.Fail("No active subscription."));

        var planName = await _db.SubscriptionPlans
            .Where(p => p.Id == sub.PlanId)
            .Select(p => p.Name)
            .FirstAsync();

        return Ok(ApiResponse<object>.Ok(sub.ToResponse(planName)));
    }

    // -------------------------------------------------------------
    // POST /api/subscriptions/change-plan  — admin
    // -------------------------------------------------------------
    [HttpPost("change-plan")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ChangePlan([FromBody] ChangePlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlanCode))
            return BadRequest(ApiResponse<object>.Fail("PlanCode is required."));

        if (!Enum.TryParse<BillingCycle>(request.BillingCycle, ignoreCase: true, out var cycle))
            return BadRequest(ApiResponse<object>.Fail("BillingCycle must be 'Monthly' or 'Yearly'."));

        var tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");

        var result = await _subs.ChangePlanAsync(tenantId, request.PlanCode, cycle);
        return Ok(ApiResponse<object>.Ok(result));
    }

    // -------------------------------------------------------------
    // POST /api/subscriptions/cancel  — admin (soft-cancel)
    // -------------------------------------------------------------
    [HttpPost("cancel")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Cancel()
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");

        await _subs.CancelAsync(tenantId);
        return Ok(ApiResponse<object>.Ok(new { Message = "Cancellation scheduled at end of current period." }));
    }

    // -------------------------------------------------------------
    // POST /api/subscriptions/resume  — admin (undo soft-cancel)
    // -------------------------------------------------------------
    [HttpPost("resume")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Resume()
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");

        await _subs.ResumeAsync(tenantId);
        return Ok(ApiResponse<object>.Ok(new { Message = "Subscription resumed." }));
    }

    // -------------------------------------------------------------
    // PATCH /api/subscriptions/payment-method  — admin
    // Update card on file or switch between auto-pay and manual.
    // -------------------------------------------------------------
    [HttpPatch("payment-method")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdatePaymentMethod([FromBody] UpdatePaymentMethodRequest request)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");

        var sub = await _subs.GetActiveAsync(tenantId);
        if (sub == null)
            return NotFound(ApiResponse<object>.Fail("No active subscription."));

        var t = (request.Type ?? "").Trim();
        if (t.Equals("Card", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.CardNumber)
                || request.ExpMonth is null or 0
                || request.ExpYear is null or 0)
                return BadRequest(ApiResponse<object>.Fail("CardNumber, ExpMonth and ExpYear are required for card payment."));

            var (brand, last4) = ExtractCardDisplay(request.CardNumber!);
            if (last4 == null)
                return BadRequest(ApiResponse<object>.Fail("Card number doesn't look right."));

            sub.SetCardPaymentMethod(
                cardBrand:         brand ?? "Card",
                cardLast4:         last4,
                expMonth:          request.ExpMonth!.Value,
                expYear:           request.ExpYear!.Value,
                holderName:        request.HolderName,
                gatewayCustomerId: request.GatewayToken);
        }
        else if (!string.IsNullOrWhiteSpace(t))
        {
            sub.SetManualPaymentMethod(t);
        }
        else
        {
            sub.ClearPaymentMethod();
        }

        await _db.SaveChangesAsync();

        var planName = await _db.SubscriptionPlans
            .Where(p => p.Id == sub.PlanId)
            .Select(p => p.Name)
            .FirstAsync();

        return Ok(ApiResponse<object>.Ok(sub.ToResponse(planName)));
    }

    private static (string? Brand, string? Last4) ExtractCardDisplay(string cardNumber)
    {
        var pan = new string(cardNumber.Where(char.IsDigit).ToArray());
        if (pan.Length < 12) return (null, null);
        var last4 = pan[^4..];
        var brand = pan switch
        {
            { Length: >= 1 } when pan.StartsWith("4") => "Visa",
            { Length: >= 2 } when pan.StartsWith("5") || pan.StartsWith("2") => "Mastercard",
            { Length: >= 2 } when pan.StartsWith("34") || pan.StartsWith("37") => "Amex",
            _ => "Card"
        };
        return (brand, last4);
    }

    // ============================================================
    // SuperAdmin plan management
    // ============================================================

    [HttpPost("plans")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> CreatePlan([FromBody] CreatePlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse<object>.Fail("Code and Name are required."));

        var code = request.Code.Trim().ToUpperInvariant();

        if (await _db.SubscriptionPlans.AnyAsync(p => p.Code == code))
            return Conflict(ApiResponse<object>.Fail("Plan code already exists."));

        var plan = new SubscriptionPlan(
            code, request.Name, request.Description,
            request.MonthlyPrice, request.YearlyPrice,
            request.TrialDays, request.Currency);

        plan.UpdateLimits(request.MaxUsers, request.MaxProducts, request.MaxOrdersPerMonth, request.MaxStorageMb);
        plan.UpdateFeatures(request.ApiAccess, request.MultiStore, request.AdvancedAnalytics, request.WebhooksEnabled, request.PrioritySupport);
        plan.SetSortOrder(request.SortOrder);

        _db.SubscriptionPlans.Add(plan);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(plan.ToResponse()));
    }

    [HttpPatch("plans/{id:guid}")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> UpdatePlan(Guid id, [FromBody] UpdatePlanRequest request)
    {
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id);
        if (plan == null)
            return NotFound(ApiResponse<object>.Fail("Plan not found."));

        if (request.Name != null || request.Description != null)
        {
            plan.UpdateNameAndDescription(request.Name ?? plan.Name, request.Description ?? plan.Description);
        }

        if (request.MonthlyPrice.HasValue || request.YearlyPrice.HasValue || request.Currency != null)
        {
            plan.UpdatePricing(
                request.MonthlyPrice ?? plan.MonthlyPrice,
                request.YearlyPrice  ?? plan.YearlyPrice,
                request.Currency     ?? plan.Currency);
        }

        if (request.TrialDays.HasValue) plan.UpdateTrialDays(request.TrialDays.Value);
        if (request.SortOrder.HasValue) plan.SetSortOrder(request.SortOrder.Value);

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value) plan.Activate();
            else plan.Deactivate();
        }

        if (request.MaxUsers.HasValue || request.MaxProducts.HasValue
            || request.MaxOrdersPerMonth.HasValue || request.MaxStorageMb.HasValue)
        {
            plan.UpdateLimits(
                request.MaxUsers          ?? plan.MaxUsers,
                request.MaxProducts       ?? plan.MaxProducts,
                request.MaxOrdersPerMonth ?? plan.MaxOrdersPerMonth,
                request.MaxStorageMb      ?? plan.MaxStorageMb);
        }

        if (request.ApiAccess.HasValue || request.MultiStore.HasValue
            || request.AdvancedAnalytics.HasValue || request.WebhooksEnabled.HasValue
            || request.PrioritySupport.HasValue)
        {
            plan.UpdateFeatures(
                request.ApiAccess         ?? plan.ApiAccess,
                request.MultiStore        ?? plan.MultiStore,
                request.AdvancedAnalytics ?? plan.AdvancedAnalytics,
                request.WebhooksEnabled   ?? plan.WebhooksEnabled,
                request.PrioritySupport   ?? plan.PrioritySupport);
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(plan.ToResponse()));
    }
}
