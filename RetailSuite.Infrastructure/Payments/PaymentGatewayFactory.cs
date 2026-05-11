using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RetailSuite.Infrastructure.Payments;

/// <summary>
/// Resolves the active <see cref="IPaymentGateway"/> based on the configured
/// <c>Payments:Provider</c> value. Unknown providers fall back to <see cref="FakePaymentGateway"/>
/// (rather than crashing) so misconfiguration is highly visible in logs but non-fatal in dev.
/// </summary>
public interface IPaymentGatewayFactory
{
    /// <summary>Return the gateway selected via configuration.</summary>
    IPaymentGateway GetActive();

    /// <summary>Resolve a specific provider by name (case-insensitive).</summary>
    IPaymentGateway GetByName(string providerName);
}

public class PaymentGatewayFactory : IPaymentGatewayFactory
{
    private readonly IServiceProvider _services;
    private readonly PaymentOptions _options;
    private readonly ILogger<PaymentGatewayFactory> _logger;

    public PaymentGatewayFactory(
        IServiceProvider services,
        IOptions<PaymentOptions> options,
        ILogger<PaymentGatewayFactory> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    public IPaymentGateway GetActive() => GetByName(_options.Provider);

    public IPaymentGateway GetByName(string providerName)
    {
        var name = (providerName ?? string.Empty).Trim();

        if (string.Equals(name, PaymentProviders.Stripe, StringComparison.OrdinalIgnoreCase))
            return Resolve<StripePaymentGateway>(name);

        if (string.Equals(name, PaymentProviders.EasyPaisa, StringComparison.OrdinalIgnoreCase))
            return Resolve<EasyPaisaPaymentGateway>(name);

        if (string.Equals(name, PaymentProviders.JazzCash, StringComparison.OrdinalIgnoreCase))
            return Resolve<JazzCashPaymentGateway>(name);

        if (string.Equals(name, PaymentProviders.Cash, StringComparison.OrdinalIgnoreCase))
            return Resolve<CashPaymentGateway>(name);

        if (string.Equals(name, PaymentProviders.Fake, StringComparison.OrdinalIgnoreCase))
            return Resolve<FakePaymentGateway>(name);

        _logger.LogWarning(
            "Unknown payment provider '{Provider}'. Falling back to FakePaymentGateway. Check appsettings 'Payments:Provider'.",
            providerName);
        return Resolve<FakePaymentGateway>(PaymentProviders.Fake);
    }

    private TGateway Resolve<TGateway>(string requested)
        where TGateway : notnull, IPaymentGateway
    {
        var gateway = (TGateway?)_services.GetService(typeof(TGateway));
        if (gateway is null)
        {
            _logger.LogError(
                "Payment provider '{Provider}' resolved to {Type} but no instance is registered in DI. Falling back to FakePaymentGateway.",
                requested, typeof(TGateway).Name);

            return (TGateway)(IPaymentGateway)
                ((FakePaymentGateway?)_services.GetService(typeof(FakePaymentGateway))
                 ?? new FakePaymentGateway());
        }
        return gateway;
    }
}
