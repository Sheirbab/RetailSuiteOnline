using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RetailSuite.Infrastructure.Payments;

/// <summary>Outcome of parsing + verifying a single webhook delivery.</summary>
public record WebhookHandleResult(
    bool Accepted,
    string? ExternalEventId,
    string? EventType,
    string? ProviderTxnRef,
    bool Succeeded,
    decimal Amount,
    string? FailureReason,
    string? RejectReason);

public interface IEasyPaisaWebhookHandler
{
    /// <summary>
    /// Parse and verify an inbound EasyPaisa webhook body. Verifies the HMAC signature
    /// in <c>secureHash</c> against the merchant HashKey. Does NOT touch the DB —
    /// reconciliation is the controller's responsibility.
    /// </summary>
    WebhookHandleResult Verify(string rawBody);
}

public class EasyPaisaWebhookHandler : IEasyPaisaWebhookHandler
{
    private readonly EasyPaisaOptions _options;
    private readonly ILogger<EasyPaisaWebhookHandler> _logger;

    public EasyPaisaWebhookHandler(
        IOptions<EasyPaisaOptions> options,
        ILogger<EasyPaisaWebhookHandler> logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    public WebhookHandleResult Verify(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(_options.HashKey))
        {
            _logger.LogWarning("EasyPaisa webhook rejected: HashKey not configured.");
            return Reject("Gateway not configured.");
        }

        if (string.IsNullOrWhiteSpace(rawBody))
            return Reject("Empty body.");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(rawBody); }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "EasyPaisa webhook: payload is not valid JSON.");
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

                if (string.Equals(prop.Name, "secureHash", StringComparison.OrdinalIgnoreCase))
                    secureHash = value;
                else
                    fields[prop.Name] = value;
            }

            if (string.IsNullOrWhiteSpace(secureHash))
                return Reject("Missing secureHash.");

            var canonical = PaymentSigning.Canonicalise(_options.HashKey!, fields);
            var expected  = PaymentSigning.HmacSha256Hex(_options.HashKey!, canonical);

            if (!string.Equals(expected, secureHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("EasyPaisa webhook signature mismatch.");
                return Reject("Invalid signature.");
            }

            // Extract fields. Field names follow Easypaisa MA callback spec at a high level —
            // adjust to your merchant pack as needed.
            var responseCode  = fields.TryGetValue("responseCode", out var rc)  ? rc  : null;
            var responseDesc  = fields.TryGetValue("responseDesc", out var rd)  ? rd  : null;
            var transactionId = fields.TryGetValue("transactionId", out var tx) ? tx  : null;
            var orderIdField  = fields.TryGetValue("orderId", out var oi)       ? oi  : null;
            var amountField   = fields.TryGetValue("transactionAmount", out var am) ? am : null;

            // Build the idempotency key. EP doesn't always issue a stable event id,
            // so we fall back to "{txnId}-{responseCode}".
            var externalEventId = string.IsNullOrWhiteSpace(transactionId)
                ? $"order-{orderIdField}-{responseCode}"
                : $"{transactionId}-{responseCode}";

            decimal amount = 0m;
            if (!string.IsNullOrWhiteSpace(amountField))
                decimal.TryParse(amountField, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);

            var succeeded = string.Equals(responseCode, "0000", StringComparison.Ordinal);

            return new WebhookHandleResult(
                Accepted:        true,
                ExternalEventId: externalEventId,
                EventType:       responseCode ?? "unknown",
                ProviderTxnRef:  transactionId,
                Succeeded:       succeeded,
                Amount:          amount,
                FailureReason:   succeeded ? null : $"EasyPaisa {responseCode}: {responseDesc}",
                RejectReason:    null);
        }
    }

    private static WebhookHandleResult Reject(string reason) =>
        new(false, null, null, null, false, 0m, null, reason);
}
