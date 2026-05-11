using RetailSuite.Infrastructure.Payments;

namespace RetailSuite.Tests.Unit;

/// <summary>
/// Verifies the canonicalisation + HMAC-SHA256 signing helpers used by the
/// EasyPaisa and JazzCash gateways. These functions are pure and deterministic,
/// so they are unit-testable without network or DI.
/// </summary>
public class PaymentSigningTests
{
    [Fact]
    public void HmacSha256Hex_ProducesStableSignatureForFixedInput()
    {
        const string key = "test-salt";
        const string payload = "test-salt&100.00&PKR";

        // Verify determinism: same inputs -> same hash.
        var sig1 = PaymentSigning.HmacSha256Hex(key, payload);
        var sig2 = PaymentSigning.HmacSha256Hex(key, payload);

        Assert.Equal(sig1, sig2);
        Assert.Equal(64, sig1.Length);        // SHA-256 hex == 64 chars
        Assert.Matches("^[a-f0-9]{64}$", sig1); // lower-case hex
    }

    [Fact]
    public void HmacSha256Hex_DifferentKeyProducesDifferentSignature()
    {
        const string payload = "anything";
        var a = PaymentSigning.HmacSha256Hex("key-A", payload);
        var b = PaymentSigning.HmacSha256Hex("key-B", payload);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Canonicalise_OrdersKeysAlphabetically_AndDropsEmptyValues()
    {
        var fields = new Dictionary<string, string?>
        {
            ["zeta"]  = "3",
            ["alpha"] = "1",
            ["beta"]  = "2",
            ["empty"] = string.Empty,   // should be excluded
            ["null"]  = null            // should be excluded
        };

        var canonical = PaymentSigning.Canonicalise("salt", fields);

        // Expected: "salt&1&2&3"  (alpha, beta, zeta sorted ordinally)
        Assert.Equal("salt&1&2&3", canonical);
    }

    [Fact]
    public void HmacSha256Hex_ThrowsOnEmptyKey()
    {
        Assert.Throws<ArgumentException>(() => PaymentSigning.HmacSha256Hex(string.Empty, "payload"));
    }
}
