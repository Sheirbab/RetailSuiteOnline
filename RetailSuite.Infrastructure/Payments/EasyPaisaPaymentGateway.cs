using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RetailSuite.Infrastructure.Payments;

/// <summary>
/// EasyPaisa payment gateway (Telenor Microfinance Bank) for Pakistani mobile payments.
/// Implements HMAC-SHA256 request signing per the Easypaisa Merchant API specification.
/// </summary>
/// <remarks>
/// Production hardening checklist (TODO before going live):
///   1. Replace stub endpoint paths ("tpg/api/transaction", "tpg/api/refund") with the live URLs
///      Easypaisa publishes in your merchant onboarding pack.
///   2. Verify the secureHash on inbound webhook callbacks before trusting status.
///   3. Add a Polly retry / circuit-breaker policy around the HttpClient.
///   4. Persist orderId -&gt; gateway transactionId mapping for reconciliation.
/// Live HTTP calls are only made when EasyPaisaOptions.IsValid; otherwise a graceful failure is returned.
/// Reference: https://developer.easypaisa.com.pk/
/// </remarks>
public class EasyPaisaPaymentGateway : IPaymentGateway
{
    private readonly ILogger<EasyPaisaPaymentGateway> _logger;
    private readonly EasyPaisaOptions _options;
    private readonly HttpClient _http;

    public EasyPaisaPaymentGateway(
        ILogger<EasyPaisaPaymentGateway> logger,
        IOptions<EasyPaisaOptions> options,
        HttpClient http)
    {
        _logger = logger;
        _options = options.Value;
        _http = http;
    }

    /// <summary>
    /// Process a payment through EasyPaisa. Flow:
    ///   1. Build canonical, sorted parameter list.
    ///   2. Sign it with HMAC-SHA256 using the merchant HashKey.
    ///   3. POST signed payload to the EasyPaisa endpoint.
    ///   4. Inspect responseCode — "0000" indicates success.
    /// </summary>
    public async Task<PaymentResult> ChargeAsync(decimal amount, string currency, string reference)
    {
        if (amount <= 0)
        {
            _logger.LogWarning("EasyPaisa: amount must be > 0 (reference={Reference}).", reference);
            return new PaymentResult(false, string.Empty, "EasyPaisa: amount must be greater than zero.");
        }

        if (!_options.IsValid)
        {
            _logger.LogWarning("EasyPaisa gateway not configured (MerchantId/HashKey/BaseUrl missing).");
            return new PaymentResult(false, string.Empty, "EasyPaisa gateway is not configured.");
        }

        try
        {
            var amountStr = amount.ToString("F2", CultureInfo.InvariantCulture);
            var expiry    = DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes).ToString("yyyyMMddHHmmss");

            // Canonical request fields. Adjust the exact field names to match your merchant pack.
            var fields = new Dictionary<string, string?>
            {
                ["storeId"]           = _options.MerchantId,
                ["orderId"]           = reference,
                ["transactionAmount"] = amountStr,
                ["transactionType"]   = "InitTransaction",
                ["postBackURL"]       = _options.WebhookUrl,
                ["expiryDate"]        = expiry
            };

            var canonical = PaymentSigning.Canonicalise(_options.HashKey!, fields);
            var signature = PaymentSigning.HmacSha256Hex(_options.HashKey!, canonical);

            var payload = new Dictionary<string, string>(
                fields.Where(kv => kv.Value is not null)
                      .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value!)))
            {
                ["secureHash"] = signature
            };

            _logger.LogInformation(
                "EasyPaisa charge prepared: OrderId={OrderId}, Amount={Amount} {Currency}",
                reference, amountStr, currency);

            // TODO: confirm the live endpoint name from EasyPaisa onboarding pack.
            var endpoint = new Uri(new Uri(_options.BaseUrl!), "tpg/api/transaction");
            using var response = await _http.PostAsJsonAsync(endpoint, payload);
            var bodyText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "EasyPaisa HTTP failure: Status={Status}, Body={Body}",
                    (int)response.StatusCode, bodyText);
                return new PaymentResult(false, string.Empty, $"EasyPaisa HTTP {(int)response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(bodyText);
            var root = doc.RootElement;
            var code = root.TryGetProperty("responseCode", out var rc) ? rc.GetString() : null;
            var desc = root.TryGetProperty("responseDesc", out var rd) ? rd.GetString() : null;
            var txn  = root.TryGetProperty("transactionId", out var tx) ? tx.GetString() : null;

            if (string.Equals(code, "0000", StringComparison.Ordinal))
            {
                _logger.LogInformation("EasyPaisa charge succeeded: TransactionId={TransactionId}", txn);
                return new PaymentResult(true, txn ?? string.Empty, null);
            }

            _logger.LogWarning("EasyPaisa charge declined: Code={Code}, Desc={Desc}", code, desc);
            return new PaymentResult(false, txn ?? string.Empty, $"EasyPaisa {code}: {desc}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EasyPaisa charge failed: Reference={Reference}", reference);
            return new PaymentResult(false, string.Empty, $"EasyPaisa error: {ex.Message}");
        }
    }

    /// <summary>
    /// Refund a previous EasyPaisa payment (full or partial).
    /// </summary>
    public async Task<PaymentResult> RefundAsync(string transactionId, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            _logger.LogWarning("EasyPaisa: transactionId required for refund.");
            return new PaymentResult(false, string.Empty, "EasyPaisa: transaction ID required for refund.");
        }

        if (!_options.IsValid)
        {
            _logger.LogWarning("EasyPaisa gateway not configured. Refund skipped.");
            return new PaymentResult(false, string.Empty, "EasyPaisa gateway is not configured.");
        }

        try
        {
            var amountStr = amount > 0
                ? amount.ToString("F2", CultureInfo.InvariantCulture)
                : "0";

            var fields = new Dictionary<string, string?>
            {
                ["storeId"]           = _options.MerchantId,
                ["originalTxnId"]     = transactionId,
                ["transactionType"]   = "Refund",
                ["transactionAmount"] = amountStr
            };

            var canonical = PaymentSigning.Canonicalise(_options.HashKey!, fields);
            var signature = PaymentSigning.HmacSha256Hex(_options.HashKey!, canonical);

            var payload = new Dictionary<string, string>(
                fields.Where(kv => kv.Value is not null)
                      .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value!)))
            {
                ["secureHash"] = signature
            };

            // TODO: confirm refund endpoint from EasyPaisa merchant docs.
            var endpoint = new Uri(new Uri(_options.BaseUrl!), "tpg/api/refund");
            using var response = await _http.PostAsJsonAsync(endpoint, payload);
            var bodyText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "EasyPaisa refund HTTP failure: Status={Status}, Body={Body}",
                    (int)response.StatusCode, bodyText);
                return new PaymentResult(false, string.Empty, $"EasyPaisa HTTP {(int)response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(bodyText);
            var root = doc.RootElement;
            var code = root.TryGetProperty("responseCode", out var rc) ? rc.GetString() : null;
            var desc = root.TryGetProperty("responseDesc", out var rd) ? rd.GetString() : null;
            var refundId = root.TryGetProperty("refundTransactionId", out var ri) ? ri.GetString() : null;

            if (string.Equals(code, "0000", StringComparison.Ordinal))
            {
                _logger.LogInformation("EasyPaisa refund succeeded: RefundId={RefundId}", refundId);
                return new PaymentResult(true, refundId ?? string.Empty, null);
            }

            _logger.LogWarning("EasyPaisa refund declined: Code={Code}, Desc={Desc}", code, desc);
            return new PaymentResult(false, refundId ?? string.Empty, $"EasyPaisa refund {code}: {desc}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EasyPaisa refund failed: TransactionId={TransactionId}", transactionId);
            return new PaymentResult(false, string.Empty, $"EasyPaisa refund error: {ex.Message}");
        }
    }
}

/// <summary>
/// Configuration options for EasyPaisa gateway.
/// Bind from appsettings.json under "EasyPaisa" section.
/// </summary>
public class EasyPaisaOptions
{
    public const string Section = "EasyPaisa";

    /// <summary>EasyPaisa-assigned merchant / store ID.</summary>
    public string? MerchantId { get; set; }

    /// <summary>Merchant hash key used to HMAC-SHA256 sign request payloads.</summary>
    public string? HashKey { get; set; }

    /// <summary>Base URL for the Easypaisa Merchant API (sandbox or production).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Callback / post-back URL the customer is redirected to after payment.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Default expiry window in minutes for hosted checkout sessions.</summary>
    public int ExpiryMinutes { get; set; } = 30;

    /// <summary>True when the gateway has enough configuration to attempt a live call.</summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(MerchantId) &&
        !string.IsNullOrWhiteSpace(HashKey) &&
        !string.IsNullOrWhiteSpace(BaseUrl);
}
