using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Identity.Entities;

/// <summary>
/// One-time, time-limited token used for email-verification and (future) password-reset flows.
/// The plaintext token is never stored — only its SHA-256 hash.
/// </summary>
public class TenantVerificationToken : TenantEntity
{
    /// <summary>The user this token authenticates.</summary>
    public Guid UserId { get; private set; }

    /// <summary>SHA-256 hash (hex) of the random token shared with the user.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>What the token authorises.</summary>
    public TokenPurpose Purpose { get; private set; }

    /// <summary>UTC expiry — caller MUST reject tokens after this time.</summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>Set when the token is redeemed; enforces one-time use.</summary>
    public DateTime? UsedAt { get; private set; }

    private TenantVerificationToken() { }

    public TenantVerificationToken(
        Guid tenantId,
        Guid userId,
        string tokenHash,
        TokenPurpose purpose,
        DateTime expiresAt)
    {
        TenantId  = tenantId;
        UserId    = userId;
        TokenHash = tokenHash;
        Purpose   = purpose;
        ExpiresAt = expiresAt;
    }

    public bool IsUsed => UsedAt.HasValue;
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsValid => !IsUsed && !IsExpired;

    public void MarkUsed() => UsedAt = DateTime.UtcNow;
}

public enum TokenPurpose
{
    VerifyEmail    = 0,
    PasswordReset  = 1,
    BillingChange  = 2
}
