using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetailSuite.Infrastructure.Payments;

namespace RetailSuite.Tests.Unit;

public class PaymentGatewaySandboxTests
{
    [Fact]
    public void Factory_ResolvesAllConfiguredGatewayTypes()
    {
        var services = new ServiceCollection();
        services.AddSingleton<FakePaymentGateway>();
        services.AddSingleton<CashPaymentGateway>();
        services.AddSingleton(new StripePaymentGateway(NullLogger<StripePaymentGateway>.Instance));
        services.AddSingleton(new EasyPaisaPaymentGateway(
            NullLogger<EasyPaisaPaymentGateway>.Instance,
            Options.Create(new EasyPaisaOptions
            {
                MerchantId = "store-1",
                HashKey = "hash-key",
                BaseUrl = "https://sandbox.easypaisa.test/"
            }),
            new HttpClient(new StubHttpMessageHandler("{}"))));
        services.AddSingleton(new JazzCashPaymentGateway(
            NullLogger<JazzCashPaymentGateway>.Instance,
            Options.Create(new JazzCashOptions
            {
                MerchantId = "merchant-1",
                Password = "password",
                IntegritySalt = "salt",
                BaseUrl = "https://sandbox.jazzcash.test/"
            }),
            new HttpClient(new StubHttpMessageHandler("{}"))));

        var factory = new PaymentGatewayFactory(
            services.BuildServiceProvider(),
            Options.Create(new PaymentOptions { Provider = PaymentProviders.Fake }),
            NullLogger<PaymentGatewayFactory>.Instance);

        Assert.IsType<FakePaymentGateway>(factory.GetByName(PaymentProviders.Fake));
        Assert.IsType<CashPaymentGateway>(factory.GetByName(PaymentProviders.Cash));
        Assert.IsType<StripePaymentGateway>(factory.GetByName(PaymentProviders.Stripe));
        Assert.IsType<EasyPaisaPaymentGateway>(factory.GetByName(PaymentProviders.EasyPaisa));
        Assert.IsType<JazzCashPaymentGateway>(factory.GetByName(PaymentProviders.JazzCash));
    }

    [Fact]
    public async Task EasyPaisa_Unconfigured_ReturnsFailureWithoutHttpCall()
    {
        var handler = new StubHttpMessageHandler("{}");
        var gateway = new EasyPaisaPaymentGateway(
            NullLogger<EasyPaisaPaymentGateway>.Instance,
            Options.Create(new EasyPaisaOptions()),
            new HttpClient(handler));

        var result = await gateway.ChargeAsync(100m, "PKR", "ORDER-1");

        Assert.False(result.Success);
        Assert.Contains("not configured", result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task EasyPaisa_ConfiguredSandboxSuccess_ReturnsGatewayTransactionId()
    {
        var handler = new StubHttpMessageHandler(
            """{"responseCode":"0000","responseDesc":"Approved","transactionId":"EP-123"}""");
        var gateway = new EasyPaisaPaymentGateway(
            NullLogger<EasyPaisaPaymentGateway>.Instance,
            Options.Create(new EasyPaisaOptions
            {
                MerchantId = "store-1",
                HashKey = "hash-key",
                BaseUrl = "https://sandbox.easypaisa.test/",
                WebhookUrl = "https://retailsuite.test/payments/easypaisa"
            }),
            new HttpClient(handler));

        var result = await gateway.ChargeAsync(125.50m, "PKR", "ORDER-2");

        Assert.True(result.Success);
        Assert.Equal("EP-123", result.TransactionId);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("https://sandbox.easypaisa.test/tpg/api/transaction", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task JazzCash_Unconfigured_ReturnsFailureWithoutHttpCall()
    {
        var handler = new StubHttpMessageHandler("{}");
        var gateway = new JazzCashPaymentGateway(
            NullLogger<JazzCashPaymentGateway>.Instance,
            Options.Create(new JazzCashOptions()),
            new HttpClient(handler));

        var result = await gateway.ChargeAsync(100m, "PKR", "ORDER-3");

        Assert.False(result.Success);
        Assert.Contains("not configured", result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task JazzCash_ConfiguredSandboxSuccess_ReturnsGatewayTransactionId()
    {
        var handler = new StubHttpMessageHandler(
            """{"pp_ResponseCode":"000","pp_ResponseMessage":"Approved","pp_TxnRefNo":"JC-123"}""");
        var gateway = new JazzCashPaymentGateway(
            NullLogger<JazzCashPaymentGateway>.Instance,
            Options.Create(new JazzCashOptions
            {
                MerchantId = "merchant-1",
                Password = "password",
                IntegritySalt = "salt",
                BaseUrl = "https://sandbox.jazzcash.test/",
                WebhookUrl = "https://retailsuite.test/payments/jazzcash"
            }),
            new HttpClient(handler));

        var result = await gateway.ChargeAsync(125.50m, "PKR", "ORDER-4");

        Assert.True(result.Success);
        Assert.Equal("JC-123", result.TransactionId);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(
            "https://sandbox.jazzcash.test/ApplicationAPI/API/Payment/DoTransaction",
            handler.LastRequestUri?.ToString());
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public StubHttpMessageHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        public int RequestCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
