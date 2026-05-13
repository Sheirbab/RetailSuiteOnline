using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RetailSuite.Infrastructure.Payments;

public interface IJazzCashWebhookHandler
{
    /// <summary>
    /// Parse and verify an inbound JazzCash callback. Verifies HMAC-SHA256
    /// of canonical pp_* fields using merchant IntegritySalt against pp_SecureHash.
    /// </summary>
    WebhookHandleResult Verify(string rawBody);
}

public class JazzCashWebhookHandler : IJazzCashWebhookHandler
{
    private readonly JazzCashOptions _options;
    private readonly ILogger<JazzCashWebhookHandler> _logger;

    public JazzCashWebhookHandler(
        IOptions<JazzCashOptions> options,
        ILogger<JazzCashWebhookHandler> logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    public WebhookHandleResult Verify(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(_options.IntegritySalt))
        {
            _logger.LogWarning("JazzCash webhook rejected: IntegritySalt not configured.");
            return Reject("Gateway not configured.");
        }

        if (string.IsNullOrWhiteSpace(rawBody))
            return Reject("Empty body.");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(rawBody); }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JazzCash webhook: payload is not valid JSON.");
            return Reject("Invalid JSON.");
        }

        using (doc)
        {
            var root = doc.RootElement;

            var fields = new Dictionary<string, string?>();
            string? secureHash = null;
            foreach (var prop in root.EnumerateObject())
            {
                var value = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString()
                    : prop.Value.ToString();

                if (string.Equals(prop.Name, "pp_SecureHash", StringComparison.OrdinalIgnoreCase))
                    secureHash = value;
                else
                    fields[prop.Name] = value;
            }

            if (string.IsNullOrWhiteSpace(secureHash))
                return Reject("Missing pp_SecureHash.");

            var canonical = PaymentSigning.Canonicalise(_options.IntegritySalt!, fields);
            var expected  = PaymentSigning.HmacSha256Hex(_options.IntegritySalt!, canonical);

            if (!string.Equals(expected, secureHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("JazzCash webhook signature mismatch.");
                return Reject("Invalid signature.");
            }

            var responseCode = fields.TryGetValue("pp_ResponseCode", out var rc) ? rc : null;
            var responseMsg  = fields.TryGetValue("pp_ResponseMessage", out var rm) ? rm : null;
            var txnRefNo     = fields.TryGetValue("pp_TxnRefNo", out var tr) ? tr : null;
            var amountField  = fields.TryGetValue("pp_Amount", out var am) ? am : null;
            var retrievalRef = fields.TryGetValue("pp_RetreivalReferenceNo", out var rr) ? rr : null;

            // JazzCash sends amount in paisas. Convert to currency units.
            decimal amount = 0m;
            if (!string.IsNullOrWhiteSpace(amountField)
                && long.TryParse(amountField, NumberStyles.Integer, CultureInfo.InvariantCulture, out var paisas))
            {
                amount = paisas / 100m;
            }

            var succeeded = string.Equals(responseCode, "000", StringComparison.Ordinal);

            // Idempotency key: TxnRefNo is unique per transaction; pair with responseCode
            // so a follow-up retry with a different result wouldn't clash.
            var externalEventId = $"{txnRefNo}-{responseCode}";

            return new WebhookHandleResult(
                Accepted:        true,
                ExternalEventId: externalEventId,
                EventType:       responseCode ?? "unknown",
                ProviderTxnRef:  txnRefNo,
                Succeeded:       succeeded,
                Amount:          amount,
                FailureReason:   succeeded ? null : $"JazzCash {responseCode}: {responseMsg}",
                RejectReason:    null);
        }
    }

    private static WebhookHandleResult Reject(string reason) =>
        new(false, null, null, null, false, 0m, null, reason);
}
