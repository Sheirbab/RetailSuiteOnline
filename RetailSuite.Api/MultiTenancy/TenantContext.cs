using RetailSuite.Shared;

namespace RetailSuite.Api.MultiTenancy;

public class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantContext(IHttpContextAccessor accessor)
    {
        _httpContextAccessor = accessor;
    }

    public Guid TenantId
    {
        get
        {
            var header = _httpContextAccessor
                .HttpContext?
                .Request
                .Headers["X-Tenant-Id"]
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(header))
                throw new Exception("Tenant header missing.");

            return Guid.Parse(header);
        }
    }
}
