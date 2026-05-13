using RetailSuite.Infrastructure.Modules.Customer.Model;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Identity.Entities
{
    public class User : TenantEntity
    {
        public string Email { get; private set; } = string.Empty;
        public string PasswordHash { get; private set; } = string.Empty;
        public UserRole Role { get; private set; }

        /// <summary>True once the user has clicked the verification link emailed at signup.</summary>
        public bool IsEmailVerified { get; private set; }

        /// <summary>UTC timestamp the user verified their email; null while pending.</summary>
        public DateTime? EmailVerifiedAt { get; private set; }

        private User() { }

        public User(Guid tenantId, string email, string passwordHash, UserRole role)
        {
            TenantId        = tenantId;
            Email           = email;
            PasswordHash    = passwordHash;
            Role            = role;
            IsEmailVerified = false;
        }

        /// <summary>Mark the user's email as verified. Idempotent.</summary>
        public void MarkEmailVerified()
        {
            if (IsEmailVerified) return;
            IsEmailVerified = true;
            EmailVerifiedAt = DateTime.UtcNow;
        }
    }
}
