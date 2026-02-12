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

            return Guid.Parse(claim.Value);
        }
    }
}
