using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RetailSuite.Infrastructure.Modules.Identity.Entities;

namespace RetailSuite.Infrastructure.Modules.Identity.Services;

/// <summary>
/// Issues and redeems one-time, time-limited verification tokens.
/// The plaintext token leaves this service exactly once (when issued).
/// Only its SHA-256 hash is persisted, so a DB leak doesn't expose live tokens.
/// </summary>
public interface IVerificationTokenService
{
    /// <summary>
    /// Issue a new token for the given user. Returns the plaintext token —
    /// it MUST be emailed and not stored anywhere else.
    /// Older tokens for the same (user, purpose) are invalidated.
    /// </summary>
    Task<string> IssueAsync(Guid tenantId, Guid userId, TokenPurpose purpose);

    /// <summary>
    /// Validate a plaintext token. Returns the matching token row if valid (unused, unexpired),
    /// or null otherwise. Does NOT mark the token as used — caller must call MarkUsedAsync
    /// after acting on it (so the transaction can commit atomically).
    /// </summary>
    Task<TenantVerificationToken?> ValidateAsync(string plaintextToken, TokenPurpose purpose);

    /// <summary>Mark a previously-validated token as consumed.</summary>
    Task MarkUsedAsync(TenantVerificationToken token);

    /// <summary>
    /// True if there is an outstanding token for (user, purpose) issued within the
    /// last <see cref="VerificationOptions.ResendCooldownSeconds"/>. Used to throttle resends.
    /// </summary>
    Task<bool> IsResendThrottledAsync(Guid userId, TokenPurpose purpose);
}

public class VerificationTokenService : IVerificationTokenService
{
    private readonly RetailDbContext _db;
    private readonly VerificationOptions _options;
    private readonly ILogger<VerificationTokenService> _logger;

    public VerificationTokenService(
        RetailDbContext db,
        IOptions<VerificationOptions> options,
        ILogger<VerificationTokenService> logger)
    {
        _db      = db;
        _options = options.Value;
        _logger  = logger;
    }

    public async Task<string> IssueAsync(Guid tenantId, Guid userId, TokenPurpose purpose)
    {
        // Invalidate any prior outstanding tokens for the same (user, purpose).
        // This guarantees only the latest emailed link works — limits replay surface.
        var outstanding = await _db.TenantVerificationTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null)
            .ToListAsync();

        foreach (var old in outstanding)
        {
            old.MarkUsed();
        }

        // Generate 32 cryptographically random bytes -> base64url (~43 chars).
        var raw = RandomNumberGenerator.GetBytes(32);
        var plaintext = Base64UrlEncode(raw);
        var hash = HashToken(plaintext);

        var token = new TenantVerificationToken(
            tenantId,
            userId,
            hash,
            purpose,
            DateTime.UtcNow.AddHours(_options.TokenTtlHours));

        _db.TenantVerificationTokens.Add(token);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Verification token issued: User={UserId}, Purpose={Purpose}, ExpiresAt={ExpiresAt}",
            userId, purpose, token.ExpiresAt);

        return plaintext;
    }

    public async Task<TenantVerificationToken?> ValidateAsync(string plaintextToken, TokenPurpose purpose)
    {
        if (string.IsNullOrWhiteSpace(plaintextToken))
            return null;

        var hash = HashToken(plaintextToken);

        var token = await _db.TenantVerificationTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose);

        if (token is null)
        {
            _logger.LogInformation("Verification token validation failed: not found.");
            return null;
        }

        if (!token.IsValid)
        {
            _logger.LogInformation(
                "Verification token validation failed: Id={TokenId}, IsUsed={IsUsed}, IsExpired={IsExpired}",
                token.Id, token.IsUsed, token.IsExpired);
            return null;
        }

        return token;
    }

    public async Task MarkUsedAsync(TenantVerificationToken token)
    {
        token.MarkUsed();

        // Re-attach if not tracked (e.g. fetched with AsNoTracking elsewhere).
        if (_db.Entry(token).State == EntityState.Detached)
            _db.TenantVerificationTokens.Update(token);

        await _db.SaveChangesAsync();
    }

    public async Task<bool> IsResendThrottledAsync(Guid userId, TokenPurpose purpose)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-_options.ResendCooldownSeconds);
        return await _db.TenantVerificationTokens
            .IgnoreQueryFilters()
            .AnyAsync(t => t.UserId == userId
                        && t.Purpose == purpose
                        && t.CreatedAt > cutoff);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static string HashToken(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var hash  = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Base64-url encoding (no padding) — URL-safe for inclusion in verification links.</summary>
    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
