using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetailSuite.Infrastructure.Payments;

namespace RetailSuite.Tests.Unit;

/// <summary>
/// Verifies the EasyPaisa webhook handler accepts only HMAC-signed bodies
/// and translates response codes correctly.
/// </summary>
public class EasyPaisaWebhookHandlerTests
{
    private const string HashKey = "test-easypaisa-hash-key";

    private static EasyPaisaWebhookHandler NewHandler() =>
        new(Options.Create(new EasyPaisaOptions
        {
            MerchantId = "M-123",
            HashKey    = HashKey,
            BaseUrl    = "https://sandbox.easypaisa.local/"
        }),
        NullLogger<EasyPaisaWebhookHandler>.Instance);

    private static string SignedPayload(Dictionary<string, string> fields, string hashKey)
    {
        var canonical = PaymentSigning.Canonicalise(
            hashKey,
            fields.ToDictionary(kv => kv.Key, kv => (string?)kv.Value));
        var sig = PaymentSigning.HmacSha256Hex(hashKey, canonical);

        var full = new Dictionary<string, string>(fields) { ["secureHash"] = sig };
        return JsonSerializer.Serialize(full);
    }

    [Fact]
    public void Verify_ValidSignedSuccessPayload_AcceptedAsSuccess()
    {
        var fields = new Dictionary<string, string>
        {
            ["responseCode"]     = "0000",
            ["responseDesc"]     = "Success",
            ["transactionId"]    = "TXN-OK-123",
            ["orderId"]          = "INV-202605-0001",
            ["transactionAmount"]= "2500.00"
        };

        var handler = NewHandler();
        var result  = handler.Verify(SignedPayload(fields, HashKey));

        Assert.True(result.Accepted);
        Assert.True(result.Succeeded);
        Assert.Equal("TXN-OK-123", result.ProviderTxnRef);
        Assert.Equal(2_500m, result.Amount);
        Assert.Equal("0000", result.EventType);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Verify_NonZeroResponseCode_AcceptedButFailed()
    {
        var fields = new Dictionary<string, string>
        {
            ["responseCode"]     = "1001",
            ["responseDesc"]     = "Insufficient balance",
            ["transactionId"]    = "TXN-FAIL-1",
            ["transactionAmount"]= "2500.00"
        };

        var handler = NewHandler();
        var result  = handler.Verify(SignedPayload(fields, HashKey));

        Assert.True(result.Accepted);
        Assert.False(result.Succeeded);
        Assert.Contains("1001", result.FailureReason);
    }

    [Fact]
    public void Verify_WrongSignature_Rejected()
    {
        var fields = new Dictionary<string, string>
        {
            ["responseCode"]  = "0000",
            ["transactionId"] = "TXN-OK-9",
            ["transactionAmount"] = "100"
        };

        // Sign with a different key so the verifier disagrees.
        var payload = SignedPayload(fields, "different-key");

        var handler = NewHandler();
        var result  = handler.Verify(payload);

        Assert.False(result.Accepted);
        Assert.Equal("Invalid signature.", result.RejectReason);
    }

    [Fact]
    public void Verify_MissingSecureHash_Rejected()
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["responseCode"]  = "0000",
            ["transactionId"] = "TXN-OK-X"
        });

        var handler = NewHandler();
        var result  = handler.Verify(payload);

        Assert.False(result.Accepted);
        Assert.Equal("Missing secureHash.", result.RejectReason);
    }

    [Fact]
    public void Verify_NonJsonBody_Rejected()
    {
        var handler = NewHandler();
        var result  = handler.Verify("not-json");
        Assert.False(result.Accepted);
        Assert.Equal("Invalid JSON.", result.RejectReason);
    }
}
