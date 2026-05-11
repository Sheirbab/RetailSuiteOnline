namespace RetailSuite.Infrastructure.Email;

/// <summary>
/// High-level orchestrator for sending business-event emails.
/// Coordinates: template rendering -> audit log persistence -> SMTP delivery.
/// All methods are best-effort: failures are logged + audited, never thrown.
/// </summary>
public interface INotificationService
{
    Task SendOrderConfirmedAsync(Guid orderId);
    Task SendOrderCancelledAsync(Guid orderId);
    Task SendPaymentReceivedAsync(Guid paymentId);
    Task SendReturnProcessedAsync(Guid orderId, decimal refundAmount);
}
