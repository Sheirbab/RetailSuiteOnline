
namespace RetailSuite.Shared
{
    public abstract class TenantEntity : BaseEntity
    {
        public Guid TenantId { get; set; }
        public bool IsDeleted { get; private set; }

        public void MarkAsDeleted()
        {
            IsDeleted = true;
        }

        public void Restore()
        {
            IsDeleted = false;
        }
    }
}
