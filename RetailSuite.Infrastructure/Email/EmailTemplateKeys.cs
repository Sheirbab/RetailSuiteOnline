namespace RetailSuite.Infrastructure.Email;

/// <summary>
/// Stable identifiers for transactional email templates.
/// Keep these as strings (not enums) so reporting / audit reads stay human-friendly.
/// </summary>
public static class EmailTemplateKeys
{
    public const string OrderConfirmed   = "ORDER_CONFIRMED";
    public const string OrderCancelled   = "ORDER_CANCELLED";
    public const string PaymentReceived  = "PAYMENT_RECEIVED";
    public const string ReturnProcessed  = "RETURN_PROCESSED";

    // Tenant lifecycle ----------------------------------------------
    public const string VerifyEmail      = "VERIFY_EMAIL";
    public const string WelcomeTenant    = "WELCOME_TENANT";

    // Subscription billing ------------------------------------------
    public const string InvoiceIssued    = "INVOICE_ISSUED";
    public const string InvoicePaid      = "INVOICE_PAID";
    public const string InvoiceOverdue   = "INVOICE_OVERDUE";
    public const string TenantSuspended  = "TENANT_SUSPENDED";
}
