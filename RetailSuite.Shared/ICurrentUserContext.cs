
namespace RetailSuite.Shared
{
    public interface ICurrentUserContext
    {
        Guid UserId { get; }
        Guid TenantId { get; }
        string Role { get; }
        bool IsAuthenticated { get; }
    }
}
