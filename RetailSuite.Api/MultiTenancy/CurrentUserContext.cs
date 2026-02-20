using RetailSuite.Shared;
using System.Security.Claims;

namespace RetailSuite.Api.MultiTenancy;

public class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    public Guid UserId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?
                .User?
                .FindFirst(ClaimTypes.NameIdentifier)?
                .Value;

            if (string.IsNullOrEmpty(value))
                throw new Exception("UserId claim missing.");

            return Guid.Parse(value);
        }
    }

    public Guid TenantId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?
                .User?
                .FindFirst("tenantId")?
                .Value;

            if (string.IsNullOrEmpty(value))
                throw new Exception("Tenant claim missing.");

            return Guid.Parse(value);
        }
    }

    public string Role =>
        _httpContextAccessor.HttpContext?
            .User?
            .FindFirst(ClaimTypes.Role)?
            .Value ?? string.Empty;
}