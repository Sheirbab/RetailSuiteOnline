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

            if (claim == null)
                return null;

            var id = Guid.Parse(claim.Value);

            // SuperAdmin users are stored with TenantId = Guid.Empty.
            // Returning null here makes the global tenant query filter a no-op,
            // so super admin can see all tenant data.
            return id == Guid.Empty ? null : id;
        }
    }
}
