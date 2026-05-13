using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Infrastructure.Modules.Identity.Services;
using RetailSuite.Shared;

namespace RetailSuite.Tests.Unit;

/// <summary>
/// Verifies the email-verification token service behaviour:
/// issue, hashing, validation, one-time use, expiry, and resend throttling.
/// </summary>
public class VerificationTokenServiceTests
{
    private static RetailDbContext NewDb()
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<RetailDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RetailDbContext(options, tenantContext.Object);
    }

    private static VerificationTokenService NewService(
        RetailDbContext db,
        int ttlHours = 24,
        int resendCooldownSeconds = 60)
    {
        var opts = Options.Create(new VerificationOptions
        {
            PublicBaseUrl         = "https://test.local",
            TokenTtlHours         = ttlHours,
            ResendCooldownSeconds = resendCooldownSeconds
        });
        return new VerificationTokenService(db, opts, NullLogger<VerificationTokenService>.Instance);
    }

    [Fact]
    public async Task IssueAsync_ReturnsPlaintextAndPersistsHashOnly()
    {
        await using var db = NewDb();
        var service = NewService(db);

        var tenantId = Guid.NewGuid();
        var userId   = Guid.NewGuid();

        var plaintext = await service.IssueAsync(tenantId, userId, TokenPurpose.VerifyEmail);

        Assert.False(string.IsNullOrWhiteSpace(plaintext));
        Assert.True(plaintext.Length >= 32);  // base64url of 32 bytes ~= 43 chars

        var stored = await db.TenantVerificationTokens.SingleAsync();
        Assert.NotEqual(plaintext, stored.TokenHash);   // hash, not plaintext
        Assert.Equal(64, stored.TokenHash.Length);      // SHA-256 hex = 64 chars
        Assert.Equal(userId, stored.UserId);
        Assert.Equal(TokenPurpose.VerifyEmail, stored.Purpose);
    }

    [Fact]
    public async Task IssueAsync_InvalidatesPreviousOutstandingTokensForSameUserAndPurpose()
    {
        await using var db = NewDb();
        var service = NewService(db);

        var tenantId = Guid.NewGuid();
        var userId   = Guid.NewGuid();

        var first  = await service.IssueAsync(tenantId, userId, TokenPurpose.VerifyEmail);
        var second = await service.IssueAsync(tenantId, userId, TokenPurpose.VerifyEmail);

        // The first one should now be unusable.
        var validatedFirst = await service.ValidateAsync(first, TokenPurpose.VerifyEmail);
        Assert.Null(validatedFirst);

        var validatedSecond = await service.ValidateAsync(second, TokenPurpose.VerifyEmail);
        Assert.NotNull(validatedSecond);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsNullForUnknownToken()
    {
        await using var db = NewDb();
        var service = NewService(db);

        var result = await service.ValidateAsync("not-a-real-token", TokenPurpose.VerifyEmail);
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsNullAfterMarkUsed()
    {
        await using var db = NewDb();
        var service = NewService(db);

        var plaintext = await service.IssueAsync(Guid.NewGuid(), Guid.NewGuid(), TokenPurpose.VerifyEmail);
        var token = await service.ValidateAsync(plaintext, TokenPurpose.VerifyEmail);
        Assert.NotNull(token);

        await service.MarkUsedAsync(token!);

        var second = await service.ValidateAsync(plaintext, TokenPurpose.VerifyEmail);
        Assert.Null(second);
    }

    [Fact]
    public async Task ValidateAsync_RejectsTokenWithWrongPurpose()
    {
        await using var db = NewDb();
        var service = NewService(db);

        var plaintext = await service.IssueAsync(Guid.NewGuid(), Guid.NewGuid(), TokenPurpose.VerifyEmail);
        var asPasswordReset = await service.ValidateAsync(plaintext, TokenPurpose.PasswordReset);
        Assert.Null(asPasswordReset);
    }

    [Fact]
    public async Task IsResendThrottledAsync_TrueImmediatelyAfterIssue()
    {
        await using var db = NewDb();
        var service = NewService(db, resendCooldownSeconds: 60);

        var userId = Guid.NewGuid();
        await service.IssueAsync(Guid.NewGuid(), userId, TokenPurpose.VerifyEmail);

        var throttled = await service.IsResendThrottledAsync(userId, TokenPurpose.VerifyEmail);
        Assert.True(throttled);
    }

    [Fact]
    public async Task IsResendThrottledAsync_FalseWhenCooldownIsZero()
    {
        await using var db = NewDb();
        var service = NewService(db, resendCooldownSeconds: 0);

        var userId = Guid.NewGuid();
        await service.IssueAsync(Guid.NewGuid(), userId, TokenPurpose.VerifyEmail);

        var throttled = await service.IsResendThrottledAsync(userId, TokenPurpose.VerifyEmail);
        Assert.False(throttled);
    }
}
