using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RetailSuite.Infrastructure.Email;

/// <summary>
/// Default implementation of INotificationService.
/// Persists every send attempt to EmailNotifications for audit/replay,
/// then delegates actual delivery to IEmailService.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly RetailDbContext _db;
    private readonly IEmailService _emailService;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        RetailDbContext db,
        IEmailService emailService,
        ILogger<NotificationService> logger)
    {
        _db = db;
        _emailService = emailService;
        _logger = logger;
    }

    // -------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------

    public async Task SendOrderConfirmedAsync(Guid orderId)
    {
        var (order, customer) = await LoadOrderCustomerAsync(orderId);
        if (order is null || customer is null || string.IsNullOrWhiteSpace(customer.Email))
            return;

        var subject = $"Order Confirmed — {order?.OrderNumber}";
        var body = EmailTemplates.OrderConfirmed(
            customer.FullName, order?.OrderNumber ?? "", order?.TotalAmount ?? 00);

        await DispatchAsync(
            customer.Email,
            subject,
            EmailTemplateKeys.OrderConfirmed,
            body,
            relatedEntityType: "Order",
            relatedEntityId: order?.Id.ToString());
    }

    public async Task SendOrderCancelledAsync(Guid orderId)
    {
        var (order, customer) = await LoadOrderCustomerAsync(orderId);
        if (order is null || customer is null || string.IsNullOrWhiteSpace(customer.Email))
            return;

        var subject = $"Order Cancelled — {order?.OrderNumber}";
        var body = EmailTemplates.OrderCancelled(
            customer.FullName, order?.OrderNumber??"", order?.TotalAmount??00);

        await DispatchAsync(
            customer.Email,
            subject,
            EmailTemplateKeys.OrderCancelled,
            body,
            relatedEntityType: "Order",
            relatedEntityId: order?.Id.ToString());
    }

    public async Task SendPaymentReceivedAsync(Guid paymentId)
    {
        var payment = await _db.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentId);

        if (payment is null)
        {
            _logger.LogWarning("Notification skipped — Payment {PaymentId} not found.", paymentId);
            return;
        }

        var (order, customer) = await LoadOrderCustomerAsync(payment.OrderId);
        if (order is null || customer is null || string.IsNullOrWhiteSpace(customer.Email))
            return;

        var subject = $"Payment Received — {order?.OrderNumber}";
        var body = EmailTemplates.PaymentReceived(
            customer.FullName,
            order?.OrderNumber,
            payment.Amount,
            payment.PaymentMethod,
            payment.TransactionReference);

        await DispatchAsync(
            customer.Email,
            subject,
            EmailTemplateKeys.PaymentReceived,
            body,
            relatedEntityType: "Payment",
            relatedEntityId: payment.Id.ToString());
    }

    public async Task SendReturnProcessedAsync(Guid orderId, decimal refundAmount)
    {
        var (order, customer) = await LoadOrderCustomerAsync(orderId);
        if (order is null || customer is null || string.IsNullOrWhiteSpace(customer.Email))
            return;

        var subject = $"Return Processed — {order?.OrderNumber}";
        var body = EmailTemplates.ReturnProcessed(
            customer.FullName, order?.OrderNumber, refundAmount);

        await DispatchAsync(
            customer.Email,
            subject,
            EmailTemplateKeys.ReturnProcessed,
            body,
            relatedEntityType: "Order",
            relatedEntityId: order?.Id.ToString());
    }

    // -------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------

    private async Task<(RetailSuite.Modules.Orders.Entities.Order? order, Modules.Customer.Entities.Customer? customer)>
        LoadOrderCustomerAsync(Guid orderId)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order is null)
        {
            _logger.LogWarning("Notification skipped — Order {OrderId} not found.", orderId);
            return (null, null);
        }

        if (order.Customer is null)
        {
            _logger.LogWarning("Notification skipped — Customer missing for Order {OrderId}.", orderId);
            return (order, null);
        }

        return (order, order.Customer);
    }

    /// <summary>
    /// Persist a pending audit row, attempt delivery, then mark sent/failed.
    /// Never throws — email is best-effort by design.
    /// </summary>
    private async Task DispatchAsync(
        string to,
        string subject,
        string templateKey,
        string htmlBody,
        string? relatedEntityType,
        string? relatedEntityId)
    {
        EmailNotification? record = null;
        try
        {
            record = new EmailNotification(
                to, subject, templateKey, htmlBody, relatedEntityType, relatedEntityId);

            _db.EmailNotifications.Add(record);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Audit persistence failed — still attempt to send so user is notified,
            // but log loudly because we've lost the audit trail.
            _logger.LogError(ex, "Failed to persist EmailNotification audit row for {To} / {TemplateKey}", to, templateKey);
        }

        try
        {
            await _emailService.SendAsync(to, subject, htmlBody);

            if (record is not null)
            {
                record.MarkSent();
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email delivery failed for {To} / {TemplateKey}", to, templateKey);

            if (record is not null)
            {
                try
                {
                    record.MarkFailed(ex.Message);
                    await _db.SaveChangesAsync();
                }
                catch (Exception persistEx)
                {
                    _logger.LogError(persistEx, "Failed to record email failure for {To} / {TemplateKey}", to, templateKey);
                }
            }
        }
    }
}
