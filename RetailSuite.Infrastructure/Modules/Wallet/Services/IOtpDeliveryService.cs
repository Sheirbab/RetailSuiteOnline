using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RetailSuite.Infrastructure.Modules.Wallet.Services;

/// <summary>
/// Delivers an OTP code to a phone number. Default impl just logs the code
/// (handy for dev / smoke tests). Plug in a real SMS provider (Jazz, Telenor,
/// Twilio, etc.) by registering a different implementation in DI.
/// </summary>
public interface IOtpDeliveryService
{
    /// <summary>Send the plaintext code to the phone. Return true if delivery is accepted.</summary>
    Task<bool> SendAsync(string phone, string code, CancellationToken ct = default);

    /// <summary>
    /// True when the impl is a "dev mode" provider that echoes the OTP back in the
    /// API response (so testers can see it without an SMS gateway). Real-SMS impls
    /// must return false so codes don't leak.
    /// </summary>
    bool IsDevMode { get; }
}

/// <summary>
/// Dev-mode OTP delivery — logs the OTP and lets the API return it in the
/// response so testers don't need an SMS gateway. NEVER use in production.
/// </summary>
public class LogOnlyOtpDelivery : IOtpDeliveryService
{
    private readonly ILogger<LogOnlyOtpDelivery> _logger;
    public LogOnlyOtpDelivery(ILogger<LogOnlyOtpDelivery> logger) => _logger = logger;

    public bool IsDevMode => true;

    public Task<bool> SendAsync(string phone, string code, CancellationToken ct = default)
    {
        _logger.LogWarning("[DEV] OTP for {Phone} = {Code}. Replace LogOnlyOtpDelivery with a real SMS provider in production.",
            phone, code);
        return Task.FromResult(true);
    }
}
