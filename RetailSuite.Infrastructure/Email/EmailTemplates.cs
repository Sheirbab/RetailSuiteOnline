using System.Globalization;
using System.Net;

namespace RetailSuite.Infrastructure.Email;

/// <summary>
/// HTML body builders for transactional emails.
/// All inputs are HTML-encoded; currency defaults to PKR (override via culture-aware caller).
/// </summary>
public static class EmailTemplates
{
    private static readonly CultureInfo PkCulture = new("en-PK");

    private static string Money(decimal amount) =>
        amount.ToString("N2", PkCulture);

    private static string E(string? s) =>
        WebUtility.HtmlEncode(s ?? string.Empty);

    private static string Shell(string title, string body) => $@"
<!DOCTYPE html>
<html><body style=""font-family:Arial,Helvetica,sans-serif;background:#f4f6f8;margin:0;padding:24px;color:#222;"">
  <div style=""max-width:600px;margin:0 auto;background:#ffffff;border-radius:8px;overflow:hidden;border:1px solid #e2e6ea;"">
    <div style=""background:#0d3b66;color:#ffffff;padding:18px 24px;"">
      <h2 style=""margin:0;font-size:20px;"">{E(title)}</h2>
    </div>
    <div style=""padding:24px;font-size:14px;line-height:1.55;"">
      {body}
    </div>
    <div style=""background:#f4f6f8;color:#6b7785;padding:14px 24px;font-size:12px;text-align:center;"">
      This is an automated message from RetailSuite. Please do not reply.
    </div>
  </div>
</body></html>";

    public static string OrderConfirmed(
        string customerName,
        string orderNumber,
        decimal totalAmount,
        string currency = "PKR")
    {
        var body = $@"
            <p>Hello <strong>{E(customerName)}</strong>,</p>
            <p>Your order has been <strong>confirmed</strong>.</p>
            <table style=""width:100%;border-collapse:collapse;margin:12px 0;"">
                <tr><td style=""padding:6px 0;color:#6b7785;"">Order #</td><td><strong>{E(orderNumber)}</strong></td></tr>
                <tr><td style=""padding:6px 0;color:#6b7785;"">Total</td><td><strong>{E(currency)} {Money(totalAmount)}</strong></td></tr>
            </table>
            <p>We will notify you once payment is received and your order is dispatched.</p>
            <p>Thank you for shopping with us.</p>";
        return Shell("Order Confirmed", body);
    }

    public static string OrderCancelled(
        string customerName,
        string orderNumber,
        decimal totalAmount,
        string currency = "PKR")
    {
        var body = $@"
            <p>Hello <strong>{E(customerName)}</strong>,</p>
            <p>Your order <strong>{E(orderNumber)}</strong> has been <strong>cancelled</strong>.</p>
            <table style=""width:100%;border-collapse:collapse;margin:12px 0;"">
                <tr><td style=""padding:6px 0;color:#6b7785;"">Order #</td><td><strong>{E(orderNumber)}</strong></td></tr>
                <tr><td style=""padding:6px 0;color:#6b7785;"">Amount</td><td>{E(currency)} {Money(totalAmount)}</td></tr>
            </table>
            <p>If any payment had already been collected, the refund will be processed back to the original payment method.</p>
            <p>For any questions, please reach out to our customer support.</p>";
        return Shell("Order Cancelled", body);
    }

    public static string PaymentReceived(
        string customerName,
        string orderNumber,
        decimal amount,
        string method,
        string? transactionRef,
        string currency = "PKR")
    {
        var txnRow = string.IsNullOrWhiteSpace(transactionRef)
            ? string.Empty
            : $@"<tr><td style=""padding:6px 0;color:#6b7785;"">Reference</td><td><code>{E(transactionRef)}</code></td></tr>";

        var body = $@"
            <p>Hello <strong>{E(customerName)}</strong>,</p>
            <p>We have received your payment. A summary is below.</p>
            <table style=""width:100%;border-collapse:collapse;margin:12px 0;"">
                <tr><td style=""padding:6px 0;color:#6b7785;"">Order #</td><td><strong>{E(orderNumber)}</strong></td></tr>
                <tr><td style=""padding:6px 0;color:#6b7785;"">Amount</td><td><strong>{E(currency)} {Money(amount)}</strong></td></tr>
                <tr><td style=""padding:6px 0;color:#6b7785;"">Method</td><td>{E(method)}</td></tr>
                {txnRow}
            </table>
            <p>Thank you for your payment.</p>";
        return Shell("Payment Received", body);
    }

    public static string ReturnProcessed(
        string customerName,
        string orderNumber,
        decimal refundAmount,
        string currency = "PKR")
    {
        var body = $@"
            <p>Hello <strong>{E(customerName)}</strong>,</p>
            <p>Your return for order <strong>{E(orderNumber)}</strong> has been processed.</p>
            <table style=""width:100%;border-collapse:collapse;margin:12px 0;"">
                <tr><td style=""padding:6px 0;color:#6b7785;"">Order #</td><td><strong>{E(orderNumber)}</strong></td></tr>
                <tr><td style=""padding:6px 0;color:#6b7785;"">Refund Amount</td><td><strong>{E(currency)} {Money(refundAmount)}</strong></td></tr>
            </table>
            <p>The refund will be credited to your original payment method within 5&ndash;7 working days.</p>";
        return Shell("Return Processed", body);
    }
}
