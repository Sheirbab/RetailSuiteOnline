using RetailSuite.Infrastructure.Modules.Customer.Model;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Identity.Entities
{
    public class User : TenantEntity
    {
        public string Email { get; private set; }
        public string PasswordHash { get; private set; }
        public UserRole Role { get; private set; }

        private User() { }

        public User(Guid tenantId, string email, string passwordHash, UserRole role)
        {
            TenantId = tenantId;
            Email = email;
            PasswordHash = passwordHash;
            Role = role;
        }
    }
}
