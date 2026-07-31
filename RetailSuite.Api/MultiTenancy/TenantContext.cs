using System.Security.Claims;
using RetailSuite.Shared;

namespace RetailSuite.Api.MultiTenancy;

public class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public TenantContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public Guid? TenantId
    {
        get
        {
            var claim = _accessor.HttpContext?
                .User?
                .Claims?
                .FirstOrDefault(c => c.Type == "tenantId");

            if (claim != null)
            {
                var id = Guid.Parse(claim.Value);

                // SuperAdmin users are stored with TenantId = Guid.Empty.
                // Returning null here makes the global tenant query filter a no-op,
                // so super admin can see all tenant data.
                return id == Guid.Empty ? null : id;
            }

            // No JWT tenant claim — this is an anonymous request (e.g. public storefront
            // browsing). Fall back to a tenant resolved from the /api/shop/{tenantSlug}
            // path segment, stashed by ResolveShopTenantFilter. Without this, the EF Core
            // global tenant query filter would be a no-op and anonymous requests would see
            // every tenant's data mixed together.
            if (_accessor.HttpContext?.Items.TryGetValue("ResolvedTenantId", out var resolved) == true
                && resolved is Guid resolvedId)
            {
                return resolvedId;
            }

            return null;
        }
    }
}
