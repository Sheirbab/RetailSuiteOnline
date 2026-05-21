using System.Security.Cryptography;
using System.Text;

namespace RetailSuite.Infrastructure.Payments;

/// <summary>
/// Helpers for signing payment-gateway requests.
/// Both Easypaisa and JazzCash require HMAC-SHA256 signatures over a
/// canonicalised, sorted, ampersand-delimited form of the request fields.
/// </summary>
public static class PaymentSigning
{
    /// <summary>
    /// Produce a hex-encoded HMAC-SHA256 of <paramref name="canonicalPayload"/>
    /// using <paramref name="key"/> as the secret.
    /// </summary>
    public static string HmacSha256Hex(string key, string canonicalPayload)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("HMAC key must not be empty.", nameof(key));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalPayload ?? string.Empty));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Build a canonical "salt&amp;key1=value1&amp;key2=value2" string from a dictionary,
    /// ordered by key (ordinal). Empty values are dropped (matches JazzCash spec).
    /// </summary>
    public static string Canonicalise(string salt, IDictionary<string, string?> fields)
    {
        var ordered = fields
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.Append(salt ?? string.Empty);
        foreach (var kv in ordered)
        {
            sb.Append('&').Append(kv.Value);
        }
        return sb.ToString();
    }
}
