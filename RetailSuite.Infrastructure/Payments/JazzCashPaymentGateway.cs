using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RetailSuite.Infrastructure.Payments;

/// <summary>
/// JazzCash payment gateway (Mobilink Microfinance Bank) for Pakistani mobile payments.
/// Implements HMAC-SHA256 secureHash signing per the JazzCash Page Redirection / Mobile Wallet API spec.
/// </summary>
/// <remarks>
/// Production hardening checklist (TODO before going live):
///   1. Replace stub endpoint paths with the live JazzCash URLs from your merchant onboarding pack.
///   2. Verify the pp_SecureHash on inbound webhook callbacks before trusting any status.
///   3. Add a Polly retry / circuit-breaker policy around the HttpClient.
///   4. Persist orderId -&gt; gateway transactionId mapping for reconciliation.
/// Live HTTP calls only when JazzCashOptions.IsValid; otherwise a graceful failure is returned.
/// Reference: https://developer.jazzcash.com.pk/
/// </remarks>
public class JazzCashPaymentGateway : IPaymentGateway
{
    private readonly ILogger<JazzCashPaymentGateway> _logger;
    private readonly JazzCashOptions _options;
    private readonly HttpClient _http;

    public JazzCashPaymentGateway(
        ILogger<JazzCashPaymentGateway> logger,
        IOptions<JazzCashOptions> options,
        HttpClient http)
    {
        _logger = logger;
        _options = options.Value;
        _http = http;
    }

    /// <summary>
    /// Process payment through JazzCash. Flow:
    ///   1. Build canonical pp_* field set per JazzCash spec.
    ///   2. Compute HMAC-SHA256 of (IntegritySalt &amp; v1 &amp; v2 ...) sorted by key.
    ///   3. POST signed payload to JazzCash, parse pp_ResponseCode.
    ///   4. "000" indicates success.
    /// </summary>
    public async Task<PaymentResult> ChargeAsync(decimal amount, string currency, string reference)
    {
        if (amount <= 0)
        {
            _logger.LogWarning("JazzCash: amount must be > 0 (reference={Reference}).", reference);
            return new PaymentResult(false, string.Empty, "JazzCash: amount must be greater than zero.");
        }

        if (!_options.IsValid)
        {
            _logger.LogWarning("JazzCash gateway not configured (MerchantId/Password/IntegritySalt/BaseUrl missing).");
            return new PaymentResult(false, string.Empty, "JazzCash gateway is not configured.");
        }

        if (!string.Equals(currency, "PKR", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "JazzCash: currency {Currency} requested; gateway is PKR-only. Proceeding will likely be rejected.",
                currency);
        }

        try
        {
            // JazzCash sends Amount in paisas (×100), formatted as integer.
            var amountPaisas = ((long)Math.Round(amount * 100m)).ToString(CultureInfo.InvariantCulture);
            var now    = DateTime.UtcNow;
            var txnDt  = now.ToString("yyyyMMddHHmmss");
            var expiry = now.AddMinutes(_options.ExpiryMinutes).ToString("yyyyMMddHHmmss");
            var txnRef = $"T{now:yyyyMMddHHmmss}{Random.Shared.Next(1000, 9999)}";

            var fields = new Dictionary<string, string?>
            {
                ["pp_Version"]            = "1.1",
                ["pp_TxnType"]            = "MWALLET",
                ["pp_Language"]           = "EN",
                ["pp_MerchantID"]         = _options.MerchantId,
                ["pp_Password"]           = _options.Password,
                ["pp_TxnRefNo"]           = txnRef,
                ["pp_Amount"]             = amountPaisas,
                ["pp_TxnCurrency"]        = _options.Currency,
                ["pp_TxnDateTime"]        = txnDt,
                ["pp_BillReference"]      = reference,
                ["pp_Description"]        = $"Order {reference}",
                ["pp_TxnExpiryDateTime"]  = expiry,
                ["pp_ReturnURL"]          = _options.WebhookUrl
            };

            var canonical = PaymentSigning.Canonicalise(_options.IntegritySalt!, fields);
            var secureHash = PaymentSigning.HmacSha256Hex(_options.IntegritySalt!, canonical);

            var payload = new Dictionary<string, string>(
                fields.Where(kv => kv.Value is not null)
                      .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value!)))
            {
                ["pp_SecureHash"] = secureHash
            };

            _logger.LogInformation(
                "JazzCash charge prepared: TxnRefNo={TxnRefNo}, BillReference={Reference}, AmountPaisa={Amount}",
                txnRef, reference, amountPaisas);

            // TODO: confirm the live endpoint name from JazzCash onboarding pack.
            var endpoint = new Uri(new Uri(_options.BaseUrl!), "ApplicationAPI/API/Payment/DoTransaction");
            using var response = await _http.PostAsJsonAsync(endpoint, payload);
            var bodyText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "JazzCash HTTP failure: Status={Status}, Body={Body}",
                    (int)response.StatusCode, bodyText);
                return new PaymentResult(false, string.Empty, $"JazzCash HTTP {(int)response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(bodyText);
            var root = doc.RootElement;
            var code = root.TryGetProperty("pp_ResponseCode", out var rc) ? rc.GetString() : null;
            var msg  = root.TryGetProperty("pp_ResponseMessage", out var rm) ? rm.GetString() : null;
            var retxn = root.TryGetProperty("pp_TxnRefNo", out var rt) ? rt.GetString() : txnRef;

            // JazzCash returns "000" for approved.
            if (string.Equals(code, "000", StringComparison.Ordinal))
            {
                _logger.LogInformation("JazzCash charge approved: TxnRefNo={TxnRefNo}", retxn);
                return new PaymentResult(true, retxn ?? txnRef, null);
            }

            _logger.LogWarning("JazzCash charge declined: Code={Code}, Msg={Msg}", code, msg);
            return new PaymentResult(false, retxn ?? txnRef, $"JazzCash {code}: {msg}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JazzCash charge failed: Reference={Reference}", reference);
            return new PaymentResult(false, string.Empty, $"JazzCash error: {ex.Message}");
        }
    }

    /// <summary>
    /// Refund a previous JazzCash payment (full or partial).
    /// </summary>
    public async Task<PaymentResult> RefundAsync(string transactionId, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            _logger.LogWarning("JazzCash: transactionId required for refund.");
            return new PaymentResult(false, string.Empty, "JazzCash: transaction ID required for refund.");
        }

        if (!_options.IsValid)
        {
            _logger.LogWarning("JazzCash gateway not configured. Refund skipped.");
            return new PaymentResult(false, string.Empty, "JazzCash gateway is not configured.");
        }

        try
        {
            var amountPaisas = amount > 0
                ? ((long)Math.Round(amount * 100m)).ToString(CultureInfo.InvariantCulture)
                : "0";

            var fields = new Dictionary<string, string?>
            {
                ["pp_Version"]    = "1.1",
                ["pp_TxnType"]    = "REFUND",
                ["pp_MerchantID"] = _options.MerchantId,
                ["pp_Password"]   = _options.Password,
                ["pp_TxnRefNo"]   = transactionId,
                ["pp_Amount"]     = amountPaisas,
                ["pp_TxnCurrency"] = _options.Currency,
                ["pp_TxnDateTime"] = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                ["pp_MerchantMPIN"] = string.Empty
            };

            var canonical = PaymentSigning.Canonicalise(_options.IntegritySalt!, fields);
            var secureHash = PaymentSigning.HmacSha256Hex(_options.IntegritySalt!, canonical);

            var payload = new Dictionary<string, string>(
                fields.Where(kv => kv.Value is not null)
                      .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value!)))
            {
                ["pp_SecureHash"] = secureHash
            };

            var endpoint = new Uri(new Uri(_options.BaseUrl!), "ApplicationAPI/API/Payment/DoRefundTransaction");
            using var response = await _http.PostAsJsonAsync(endpoint, payload);
            var bodyText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "JazzCash refund HTTP failure: Status={Status}, Body={Body}",
                    (int)response.StatusCode, bodyText);
                return new PaymentResult(false, string.Empty, $"JazzCash HTTP {(int)response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(bodyText);
            var root = doc.RootElement;
            var code = root.TryGetProperty("pp_ResponseCode", out var rc) ? rc.GetString() : null;
            var msg  = root.TryGetProperty("pp_ResponseMessage", out var rm) ? rm.GetString() : null;
            var refundRef = root.TryGetProperty("pp_RetreivalReferenceNo", out var rr) ? rr.GetString() : null;

            if (string.Equals(code, "000", StringComparison.Ordinal))
            {
                _logger.LogInformation("JazzCash refund approved: RetrievalRef={RetrievalRef}", refundRef);
                return new PaymentResult(true, refundRef ?? string.Empty, null);
            }

            _logger.LogWarning("JazzCash refund declined: Code={Code}, Msg={Msg}", code, msg);
            return new PaymentResult(false, refundRef ?? string.Empty, $"JazzCash refund {code}: {msg}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JazzCash refund failed: TransactionId={TransactionId}", transactionId);
            return new PaymentResult(false, string.Empty, $"JazzCash refund error: {ex.Message}");
        }
    }
}

/// <summary>
/// Configuration options for JazzCash gateway.
/// Bind from appsettings.json under "JazzCash" section.
/// </summary>
public class JazzCashOptions
{
    public const string Section = "JazzCash";

    /// <summary>JazzCash-issued merchant ID.</summary>
    public string? MerchantId { get; set; }

    /// <summary>Merchant password assigned by JazzCash.</summary>
    public string? Password { get; set; }

    /// <summary>Integrity salt used to HMAC-SHA256 sign request payloads.</summary>
    public string? IntegritySalt { get; set; }

    /// <summary>Base URL for the JazzCash API (sandbox or production).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Return / webhook URL after a hosted checkout completes.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>ISO 4217 currency code; JazzCash defaults to PKR.</summary>
    public string Currency { get; set; } = "PKR";

    /// <summary>Hosted checkout session expiry, minutes.</summary>
    public int ExpiryMinutes { get; set; } = 30;

    /// <summary>True when the gateway has enough configuration to attempt a live call.</summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(MerchantId) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(IntegritySalt) &&
        !string.IsNullOrWhiteSpace(BaseUrl);
}
