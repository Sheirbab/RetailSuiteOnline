using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Wallet.Entities;

/// <summary>
/// A short-lived OTP token issued to a phone number so the holder can
/// authenticate to the customer wallet. Stored hashed (never plaintext) and
/// invalidated after first successful verify, expiry, or after too many failed attempts.
/// </summary>
public class CustomerOtpToken : TenantEntity
{
    public string Phone { get; private set; } = string.Empty;

    /// <summary>SHA-256 hex of the OTP code. Plaintext is only ever held in memory while delivering.</summary>
    public string CodeHash { get; private set; } = string.Empty;

    public DateTime ExpiresAt { get; private set; }
    public DateTime? UsedAt { get; private set; }
    public int AttemptCount { get; private set; }

    public bool IsActive => UsedAt == null && DateTime.UtcNow < ExpiresAt && AttemptCount < 5;

    private CustomerOtpToken() { }

    public CustomerOtpToken(Guid tenantId, string phone, string codeHash, TimeSpan validFor)
    {
        if (string.IsNullOrWhiteSpace(phone))
            throw new ArgumentException("Phone is required.", nameof(phone));
        if (string.IsNullOrWhiteSpace(codeHash))
            throw new ArgumentException("CodeHash is required.", nameof(codeHash));

        Id        = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        TenantId  = tenantId;
        Phone     = phone.Trim();
        CodeHash  = codeHash;
        ExpiresAt = CreatedAt.Add(validFor);
    }

    public void RecordAttempt() => AttemptCount++;
    public void MarkUsed() => UsedAt = DateTime.UtcNow;
}
