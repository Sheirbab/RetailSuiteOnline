using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Tenant.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Api.MultiTenancy;

/// <summary>
/// Resolves the tenant for anonymous public-storefront requests from the "{tenantSlug}"
/// route segment (e.g. /api/shop/demo-store/products), and stashes it in HttpContext.Items
/// so <see cref="TenantContext"/> can fall back to it when there's no JWT tenant claim.
/// Applied to <see cref="Controllers.ShopController"/> via [ServiceFilter].
/// </summary>
public class ResolveShopTenantFilter : IAsyncActionFilter
{
    private const string CacheKeyPrefix = "shop-tenant-slug:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly RetailDbContext _db;
    private readonly IMemoryCache _cache;

    public ResolveShopTenantFilter(RetailDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.ActionArguments.TryGetValue("tenantSlug", out var slugObj)
            || slugObj is not string slug
            || string.IsNullOrWhiteSpace(slug))
        {
            context.Result = new BadRequestObjectResult(ApiResponse<object>.Fail("Store slug is required."));
            return;
        }

        var normalizedSlug = slug.Trim().ToLowerInvariant();

        var tenantId = await _cache.GetOrCreateAsync(CacheKeyPrefix + normalizedSlug, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _db.Tenants
                .Where(t => t.Subdomain == normalizedSlug
                         && t.Status != TenantStatus.Cancelled
                         && t.Status != TenantStatus.Suspended)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync();
        });

        if (tenantId == null)
        {
            context.Result = new NotFoundObjectResult(ApiResponse<object>.Fail("Store not found."));
            return;
        }

        context.HttpContext.Items["ResolvedTenantId"] = tenantId.Value;

        await next();
    }
}
