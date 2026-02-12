namespace RetailSuite.Shared;

public interface ITenantContext
{
    Guid? TenantId { get; }
}