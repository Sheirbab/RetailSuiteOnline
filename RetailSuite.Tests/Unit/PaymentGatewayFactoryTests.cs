using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetailSuite.Infrastructure.Payments;

namespace RetailSuite.Tests.Unit;

/// <summary>
/// Verifies that PaymentGatewayFactory selects the correct gateway type
/// based on the Payments:Provider configuration string.
/// </summary>
public class PaymentGatewayFactoryTests
{
    private static IPaymentGatewayFactory BuildFactory(string provider)
    {
        var services = new ServiceCollection();

        // Register all gateway implementations the factory may resolve.
        services.AddSingleton<FakePaymentGateway>();
        services.AddSingleton<CashPaymentGateway>();
        // EasyPaisa / JazzCash / Stripe constructors require options + dependencies,
        // which are not needed for the provider-selection assertions below.

        var options = Options.Create(new PaymentOptions { Provider = provider });
        var logger  = NullLogger<PaymentGatewayFactory>.Instance;

        var provider2 = services.BuildServiceProvider();
        return new PaymentGatewayFactory(provider2, options, logger);
    }

    [Fact]
    public void GetActive_Cash_ReturnsCashGateway()
    {
        var factory = BuildFactory(PaymentProviders.Cash);
        var gateway = factory.GetActive();
        Assert.IsType<CashPaymentGateway>(gateway);
    }

    [Fact]
    public void GetActive_Fake_ReturnsFakeGateway()
    {
        var factory = BuildFactory(PaymentProviders.Fake);
        var gateway = factory.GetActive();
        Assert.IsType<FakePaymentGateway>(gateway);
    }

    [Fact]
    public void GetActive_UnknownProvider_FallsBackToFakeGateway()
    {
        var factory = BuildFactory("SomeProviderThatDoesNotExist");
        var gateway = factory.GetActive();
        Assert.IsType<FakePaymentGateway>(gateway);
    }

    [Fact]
    public void GetByName_IsCaseInsensitive()
    {
        var factory = BuildFactory(PaymentProviders.Fake);
        Assert.IsType<CashPaymentGateway>(factory.GetByName("cash"));
        Assert.IsType<CashPaymentGateway>(factory.GetByName("CASH"));
        Assert.IsType<CashPaymentGateway>(factory.GetByName("Cash"));
    }
}
