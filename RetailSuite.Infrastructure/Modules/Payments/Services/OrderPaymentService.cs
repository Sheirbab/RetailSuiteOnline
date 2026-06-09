using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Payments.Entities;
using RetailSuite.Infrastructure.Payments;

namespace RetailSuite.Infrastructure.Modules.Payments.Services;

public interface IOrderPaymentService
{
    /// <summary>
    /// Create a payment intent for the order against the named provider, attempt to
    /// initiate the charge with the gateway, and return the intent with its QR payload.
    /// </summary>
    Task<OrderPaymentIntent> CreateIntentAsync(Guid orderId, string provider, decimal amount);

    /// <summary>
    /// Mark an intent paid (called from webhook handler). Idempotent — re-applying a
    /// successful webhook is a no-op.
    /// </summary>
    Task<OrderPaymentIntent?> MarkIntentPaidByGatewayTxnAsync(
        string provider, string providerTxnId, string? rawPayload);

    Task<OrderPaymentIntent?> GetActiveAsync(Guid orderId);
}

public class OrderPaymentService : IOrderPaymentService
{
    private readonly RetailDbContext _db;
    private readonly IServiceProvider _services;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        RetailDbContext db,
        IServiceProvider services,
        ILogger<OrderPaymentService> logger)
    {
        _db        = db;
        _services  = services;
        _logger    = logger;
    }

    public async Task<OrderPaymentIntent> CreateIntentAsync(Guid orderId, string provider, decimal amount)
    {
        if (amount <= 0)
            throw new BusinessRuleException("Payment amount must be positive.");

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new NotFoundException("Order", orderId);

        // Expire any prior Pending intents for this order to keep things tidy.
        var stale = await _db.OrderPaymentIntents
            .Where(i => i.OrderId == orderId && i.Status == PaymentIntentStatus.Pending)
            .ToListAsync();
        foreach (var s in stale) s.MarkExpired();

        var intent = new OrderPaymentIntent(order.TenantId, orderId, provider, amount, "PKR");
        _db.OrderPaymentIntents.Add(intent);
        await _db.SaveChangesAsync();

        // Ask the gateway to initiate the charge. The gateway returns a txn-id we
        // can embed in the QR for the customer to scan / the gateway to match later.
        var reference = $"{order.OrderNumber}:{intent.Id}";
        var gateway = ResolveGateway(provider);
        if (gateway != null)
        {
            var result = await gateway.ChargeAsync(amount, intent.Currency, reference);
            if (result.Success || !string.IsNullOrEmpty(result.TransactionId))
            {
                // Even on a sandbox / not-configured gateway we still get a TransactionId
                // back so the QR has something deterministic to encode.
                var txnId = string.IsNullOrEmpty(result.TransactionId) ? Guid.NewGuid().ToString("N") : result.TransactionId;
                var qr    = BuildQrPayload(provider, txnId, amount, intent.Currency);
                intent.SetGatewayTransaction(txnId, qr);

                if (!result.Success)
                {
                    _logger.LogWarning(
                        "Gateway {Provider} returned non-success but we kept the QR for sandbox flow: {Error}",
                        provider, result.Error);
                }
            }
            else
            {
                // No txnId either — mark failed.
                intent.MarkFailed(result.Error ?? "Gateway did not return a transaction id.");
            }
        }
        else
        {
            // No gateway implementation registered for this provider — generate a deterministic
            // fake QR so the dev experience works end-to-end. Webhook simulator can mark it paid.
            var txnId = Guid.NewGuid().ToString("N");
            intent.SetGatewayTransaction(txnId, BuildQrPayload(provider, txnId, amount, intent.Currency));
        }

        await _db.SaveChangesAsync();
        return intent;
    }

    public async Task<OrderPaymentIntent?> MarkIntentPaidByGatewayTxnAsync(
        string provider, string providerTxnId, string? rawPayload)
    {
        var intent = await _db.OrderPaymentIntents
            .Where(i => i.Provider == provider && i.ProviderTxnId == providerTxnId)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync();
        if (intent == null)
        {
            _logger.LogWarning(
                "Webhook for {Provider} txn={Txn} has no matching payment intent — dropping.",
                provider, providerTxnId);
            return null;
        }

        if (intent.Status == PaymentIntentStatus.Paid) return intent; // idempotent

        intent.MarkPaid(providerTxnId);

        // Register the payment on the order itself.
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == intent.OrderId);
        if (order != null)
        {
            order.RegisterPayment(intent.AmountDue);
            order.SetPaymentMethod(provider);
            if (order.IsFullyPaid) order.Complete();

            _db.Payments.Add(new RetailSuite.Modules.Accounting.Entities.Payment(order.Id, intent.AmountDue, provider));
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Payment intent {IntentId} marked Paid via {Provider}. Order {OrderNumber} now {Status}.",
            intent.Id, provider, order?.OrderNumber, order?.Status);

        return intent;
    }

    public Task<OrderPaymentIntent?> GetActiveAsync(Guid orderId) =>
        _db.OrderPaymentIntents
           .Where(i => i.OrderId == orderId && i.Status == PaymentIntentStatus.Pending)
           .OrderByDescending(i => i.CreatedAt)
           .FirstOrDefaultAsync();

    // ----- helpers ------------------------------------------------------

    /// <summary>Resolve the IPaymentGateway implementation for a provider name.</summary>
    private IPaymentGateway? ResolveGateway(string provider) => provider.ToLowerInvariant() switch
    {
        "easypaisa" => _services.GetService(typeof(EasyPaisaPaymentGateway)) as IPaymentGateway,
        "jazzcash"  => _services.GetService(typeof(JazzCashPaymentGateway))  as IPaymentGateway,
        _ => null
    };

    /// <summary>
    /// Build the QR payload encoded into the displayed QR. Format is
    /// "RS|{provider}|{txnId}|{amount}|{currency}" — simple enough to parse from a
    /// scan-simulator endpoint during dev. Replace with the gateway-issued QR string
    /// once live credentials are wired (EP/JC issue their own QR data formats).
    /// </summary>
    private static string BuildQrPayload(string provider, string txnId, decimal amount, string currency) =>
        $"RS|{provider}|{txnId}|{amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}|{currency}";
}
