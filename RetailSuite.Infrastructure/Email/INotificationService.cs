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

    // ----- Tenant lifecycle ----------------------------------------
    Task SendVerifyEmailAsync(
        string toAddress,
        string recipientName,
        string tenantName,
        string verificationUrl,
        int expiryHours,
        Guid? tenantId = null,
        Guid? userId = null);

    Task SendWelcomeTenantAsync(
        string toAddress,
        string recipientName,
        string tenantName,
        string loginUrl,
        Guid? tenantId = null);

    // ----- Subscription billing -----------------------------------
    Task SendInvoiceIssuedAsync(
        string toAddress, string recipientName, string tenantName,
        string invoiceNumber, decimal amount, string currency, DateTime dueDate, string payUrl,
        Guid? tenantId = null, Guid? invoiceId = null);

    Task SendInvoicePaidAsync(
        string toAddress, string recipientName, string tenantName,
        string invoiceNumber, decimal amount, string currency, string method,
        Guid? tenantId = null, Guid? invoiceId = null);

    Task SendInvoiceOverdueAsync(
        string toAddress, string recipientName, string tenantName,
        string invoiceNumber, decimal amount, string currency, DateTime dueDate, string payUrl,
        Guid? tenantId = null, Guid? invoiceId = null);

    Task SendTenantSuspendedAsync(
        string toAddress, string recipientName, string tenantName,
        string invoiceNumber, string payUrl,
        Guid? tenantId = null, Guid? invoiceId = null);
}
