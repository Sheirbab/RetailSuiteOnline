using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetailSuite.Infrastructure.Payments;

namespace RetailSuite.Tests.Unit;

/// <summary>
/// Verifies the JazzCash webhook handler validates pp_SecureHash correctly
/// and converts pp_Amount (paisas) to currency units.
/// </summary>
public class JazzCashWebhookHandlerTests
{
    private const string IntegritySalt = "test-jazzcash-integrity-salt";

    private static JazzCashWebhookHandler NewHandler() =>
        new(Options.Create(new JazzCashOptions
        {
            MerchantId    = "MC123",
            Password      = "password",
            IntegritySalt = IntegritySalt,
            BaseUrl       = "https://sandbox.jazzcash.local/"
        }),
        NullLogger<JazzCashWebhookHandler>.Instance);

    private static string SignedPayload(Dictionary<string, string> fields, string salt)
    {
        var canonical = PaymentSigning.Canonicalise(
            salt,
            fields.ToDictionary(kv => kv.Key, kv => (string?)kv.Value));
        var sig = PaymentSigning.HmacSha256Hex(salt, canonical);

        var full = new Dictionary<string, string>(fields) { ["pp_SecureHash"] = sig };
        return JsonSerializer.Serialize(full);
    }

    [Fact]
    public void Verify_ValidSignedSuccess_ConvertsPaisasToRupees()
    {
        var fields = new Dictionary<string, string>
        {
            ["pp_ResponseCode"]    = "000",
            ["pp_ResponseMessage"] = "Approved",
            ["pp_TxnRefNo"]        = "T20260514120000XYZ",
            ["pp_Amount"]          = "250000",   // 2500.00 PKR in paisas
            ["pp_TxnCurrency"]     = "PKR"
        };

        var handler = NewHandler();
        var result  = handler.Verify(SignedPayload(fields, IntegritySalt));

        Assert.True(result.Accepted);
        Assert.True(result.Succeeded);
        Assert.Equal("T20260514120000XYZ", result.ProviderTxnRef);
        Assert.Equal(2_500m, result.Amount);
    }

    [Fact]
    public void Verify_NonZeroResponseCode_AcceptedButFailed()
    {
        var fields = new Dictionary<string, string>
        {
            ["pp_ResponseCode"]    = "121",
            ["pp_ResponseMessage"] = "Transaction Declined",
            ["pp_TxnRefNo"]        = "T-FAIL",
            ["pp_Amount"]          = "250000"
        };

        var handler = NewHandler();
        var result  = handler.Verify(SignedPayload(fields, IntegritySalt));

        Assert.True(result.Accepted);
        Assert.False(result.Succeeded);
        Assert.Contains("121", result.FailureReason);
    }

    [Fact]
    public void Verify_WrongSignature_Rejected()
    {
        var fields = new Dictionary<string, string>
        {
            ["pp_ResponseCode"] = "000",
            ["pp_TxnRefNo"]     = "T-OK",
            ["pp_Amount"]       = "100"
        };

        var payload = SignedPayload(fields, "different-salt");
        var handler = NewHandler();
        var result  = handler.Verify(payload);

        Assert.False(result.Accepted);
        Assert.Equal("Invalid signature.", result.RejectReason);
    }

    [Fact]
    public void Verify_NotConfigured_Rejected()
    {
        var handler = new JazzCashWebhookHandler(
            Options.Create(new JazzCashOptions()),   // IntegritySalt empty
            NullLogger<JazzCashWebhookHandler>.Instance);

        var result = handler.Verify("{\"pp_SecureHash\":\"x\"}");
        Assert.False(result.Accepted);
        Assert.Equal("Gateway not configured.", result.RejectReason);
    }
}
