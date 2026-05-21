using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Customer.Services;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Tenant-level loyalty configuration. Admin-only.
/// </summary>
[ApiController]
[Route("api/loyalty/settings")]
[Authorize(Policy = "AdminOnly")]
public class LoyaltySettingsController : ControllerBase
{
    private readonly ILoyaltyService _loyalty;
    private readonly ITenantContext _tenantContext;

    public LoyaltySettingsController(ILoyaltyService loyalty, ITenantContext tenantContext)
    {
        _loyalty = loyalty;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var tenantId = RequireTenantId();
        var s = await _loyalty.GetSettingsAsync(tenantId);
        return Ok(ApiResponse<object>.Ok(new
        {
            s.RupeesPerPoint,
            s.MinRedeemPoints,
            s.PointValueRupees,
            s.MaxRedemptionPercentOfOrder,
            s.IsEnabled
        }));
    }

    [HttpPatch]
    public async Task<IActionResult> Update([FromBody] LoyaltySettingsRequest request)
    {
        var s = await _loyalty.UpdateSettingsAsync(
            RequireTenantId(),
            request.RupeesPerPoint,
            request.MinRedeemPoints,
            request.PointValueRupees,
            request.MaxRedemptionPercentOfOrder,
            request.IsEnabled);

        return Ok(ApiResponse<object>.Ok(new
        {
            s.RupeesPerPoint,
            s.MinRedeemPoints,
            s.PointValueRupees,
            s.MaxRedemptionPercentOfOrder,
            s.IsEnabled
        }));
    }

    private Guid RequireTenantId() =>
        _tenantContext.TenantId
        ?? throw new UnauthorizedAccessException("Tenant context missing.");
}

public class LoyaltySettingsRequest
{
    public decimal? RupeesPerPoint { get; set; }
    public int?     MinRedeemPoints { get; set; }
    public decimal? PointValueRupees { get; set; }
    public decimal? MaxRedemptionPercentOfOrder { get; set; }
    public bool?    IsEnabled { get; set; }
}
