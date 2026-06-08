using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure.Modules.Wallet.Entities;

// Customer is both a namespace and an entity class — alias the class to avoid the collision.
using CustomerEntity = RetailSuite.Infrastructure.Modules.Customer.Entities.Customer;

namespace RetailSuite.Infrastructure.Modules.Wallet.Services;

public interface IOtpService
{
    /// <summary>
    /// Issue a fresh OTP for the phone. Resolves the tenant by looking up the phone
    /// (must match exactly one customer record across tenants), generates a 6-digit code,
    /// stores it hashed, and dispatches via <see cref="IOtpDeliveryService"/>.
    /// In dev mode the plaintext code is returned for testing.
    /// </summary>
    Task<OtpRequestResult> RequestAsync(string phone, CancellationToken ct = default);

    /// <summary>
    /// Verify a submitted code for the phone. Returns the matching Customer + tenantId on success.
    /// Increments the token's attempt counter on failure; invalidates after 5 wrong tries or expiry.
    /// </summary>
    Task<(CustomerEntity Customer, Guid TenantId)?> VerifyAsync(string phone, string code, CancellationToken ct = default);
}

public record OtpRequestResult(bool Sent, string? DevOtp, string Message);

public class OtpService : IOtpService
{
    private readonly RetailDbContext _db;
    private readonly IOtpDeliveryService _delivery;

    private static readonly TimeSpan OtpValidFor = TimeSpan.FromMinutes(10);

    public OtpService(RetailDbContext db, IOtpDeliveryService delivery)
    {
        _db       = db;
        _delivery = delivery;
    }

    public async Task<OtpRequestResult> RequestAsync(string phone, CancellationToken ct = default)
    {
        var normalised = NormalisePhone(phone);
        if (normalised == null)
            return new OtpRequestResult(false, null, "Invalid phone number.");

        // Resolve tenant by phone uniqueness. The customer must exist in exactly
        // one tenant — typical assumption since each phone belongs to one shop.
        var matches = await _db.Customers
            .IgnoreQueryFilters()
            .Where(c => c.Phone == normalised)
            .Select(c => new { c.Id, c.TenantId })
            .ToListAsync(ct);

        if (matches.Count == 0)
            return new OtpRequestResult(false, null,
                "No customer record matches that phone. Make a purchase at the store first.");
        if (matches.Count > 1)
            return new OtpRequestResult(false, null,
                "Phone is registered with multiple shops — contact the store admin.");

        var tenantId = matches[0].TenantId;

        // Throttle: if an active token exists with < 60s of life, reuse it (don't blast SMS).
        var existing = await _db.CustomerOtpTokens
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.Phone == normalised && t.UsedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (existing != null && existing.IsActive && (DateTime.UtcNow - existing.CreatedAt).TotalSeconds < 60)
            return new OtpRequestResult(true, null, "OTP already sent recently — check your messages.");

        var code = GenerateCode();
        var token = new CustomerOtpToken(tenantId, normalised, Hash(code), OtpValidFor);
        _db.CustomerOtpTokens.Add(token);
        await _db.SaveChangesAsync(ct);

        var sent = await _delivery.SendAsync(normalised, code, ct);
        return new OtpRequestResult(
            sent,
            _delivery.IsDevMode ? code : null,
            sent ? "OTP sent." : "Failed to send OTP — try again.");
    }

    public async Task<(CustomerEntity Customer, Guid TenantId)?> VerifyAsync(string phone, string code, CancellationToken ct = default)
    {
        var normalised = NormalisePhone(phone);
        if (normalised == null || string.IsNullOrWhiteSpace(code)) return null;

        // Find the most recent active token for this phone across tenants.
        var token = await _db.CustomerOtpTokens
            .IgnoreQueryFilters()
            .Where(t => t.Phone == normalised && t.UsedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (token == null || !token.IsActive) return null;

        token.RecordAttempt();
        var hashed = Hash(code.Trim());
        if (!FixedTimeEquals(hashed, token.CodeHash))
        {
            await _db.SaveChangesAsync(ct);
            return null;
        }

        token.MarkUsed();

        var customer = await _db.Customers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == token.TenantId && c.Phone == normalised, ct);

        await _db.SaveChangesAsync(ct);
        return customer == null ? null : (customer, token.TenantId);
    }

    // ----- helpers ----------------------------------------------------------

    private static string GenerateCode()
    {
        // 6-digit numeric using a cryptographically strong RNG.
        Span<byte> buf = stackalloc byte[4];
        RandomNumberGenerator.Fill(buf);
        var n = BitConverter.ToUInt32(buf) % 1_000_000u;
        return n.ToString("D6");
    }

    private static string Hash(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var ba = Encoding.ASCII.GetBytes(a);
        var bb = Encoding.ASCII.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    /// <summary>Strip spaces/dashes; keep last 11 digits to align with Customer.Phone storage.</summary>
    private static string? NormalisePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 10) return null;
        return digits.Length > 11 ? digits[^11..] : digits;
    }
}
