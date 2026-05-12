# Webhook Email Notifications

## Overview
The webhook handler now sends email notifications to customers whenever payment events occur. This enhances the customer experience by providing immediate confirmation or failure notifications.

## Implementation Details

### Email Service Integration
The `StripeWebhookHandler` now injects `IEmailService` to send emails on payment events:

```csharp
public StripeWebhookHandler(
    ILogger<StripeWebhookHandler> logger,
    IEmailService emailService)
{
    _logger = logger;
    _emailService = emailService;
}
```

### Payment Events Handled

#### 1. Payment Confirmation (charge.succeeded)
- **Trigger**: When a Stripe charge succeeds
- **Recipient**: Customer email from charge metadata
- **Subject**: `Payment Confirmation - Order {OrderNumber}`
- **Content**: 
  - Order number
  - Amount paid (formatted with currency)
  - Transaction ID
  - Payment date/time
  - Professional HTML styling with green header

#### 2. Payment Failure (charge.failed)
- **Trigger**: When a Stripe charge fails
- **Recipient**: Customer email from charge metadata
- **Subject**: `Payment Failed - Order {OrderNumber}`
- **Content**:
  - Order number
  - Amount attempted (formatted with currency)
  - Failure reason (from Stripe)
  - Troubleshooting suggestions:
    - Check card details
    - Ensure sufficient funds
    - Try different payment method
    - Contact bank if issue persists
  - Professional HTML styling with red header

#### 3. Refund Confirmation (charge.refunded)
- **Trigger**: When a charge is refunded
- **Recipient**: Customer email from charge metadata
- **Subject**: `Refund Confirmation - Order {OrderNumber}`
- **Content**:
  - Order number
  - Refund amount (formatted with currency)
  - Transaction ID
  - Refund date/time
  - Processing time (3-5 business days)
  - Professional HTML styling with blue header

### Email Sending Configuration

Emails are sent using the configured SMTP settings in `appsettings.json`:

```json
"Email": {
  "Host": "",
  "Port": 587,
  "From": "noreply@retailsuite.com",
  "Username": "",
  "Password": "",
  "EnableSsl": true
}
```

**Development Mode**: If `Email:Host` is empty (default), emails are logged as "skipped" - no SMTP server needed.

**Production Mode**: Configure real SMTP credentials for live email delivery.

### Customer Email Extraction

The handler extracts customer email from charge metadata:

```csharp
var customerEmail = charge.Metadata?.ContainsKey("customer_email") == true
    ? charge.Metadata["customer_email"]
    : null;
```

**Important**: When creating charges via `StripePaymentGateway`, ensure customer email is included in metadata:

```csharp
var createOptions = new ChargeCreateOptions
{
    Amount = (long)(amount * 100), // Convert to cents
    Currency = currency.ToLower(),
    Source = "tok_visa", // In production, use real token
    Description = reference,
    Metadata = new Dictionary<string, string>
    {
        { "customer_email", customerEmail },
        { "order_id", orderId }
    }
};
```

### Email HTML Templates

All email templates are generated with:
- Professional styling with Flexbox layouts
- Clear typography and spacing
- Responsive design (max-width: 600px)
- Color-coded headers (green=success, red=failure, blue=info)
- Inline CSS for maximum email client compatibility
- Proper HTML5 doctype and UTF-8 encoding

## Logging

All email operations are logged with structured logging:

```
[Information] Sending payment confirmation email to {Email}
[Warning] No customer email found for charge {ChargeId}. Consider adding to metadata.
[Information] Successfully processed Stripe webhook event: Type=charge.succeeded, Id={EventId}
```

## Error Handling

Email sending failures are handled gracefully:
- Failures are logged but never propagate to the webhook handler
- `IEmailService.SendAsync` is designed to be best-effort
- Webhook processing continues even if email sending fails
- Admin can see failed emails in logs for manual follow-up

## Testing

### In Development
1. Leave `Email:Host` empty in appsettings.json
2. Emails are logged as "skipped" - check application logs
3. Verify charge.succeeded/charge.failed/charge.refunded event routing works

### In Staging/Production
1. Configure real SMTP credentials in `Email:*` settings
2. Test with test Stripe account and test cards
3. Verify emails arrive in customer inbox
4. Monitor logs for any failures

## Related Components

- **IEmailService**: Abstraction for email sending
- **SmtpEmailService**: SMTP implementation
- **INotificationService**: High-level orchestrator for business events
- **StripeWebhookHandler**: Processes Stripe events and triggers emails
- **WebhookController**: Endpoint for Stripe webhook delivery

## Future Enhancements

- [ ] Template engine integration (Razor, Scriban, etc.)
- [ ] Email template versioning
- [ ] Customer preference for email notifications
- [ ] Retry logic for failed email sends
- [ ] Email analytics (opens, clicks)
- [ ] Internationalization (i18n) for email content
- [ ] Admin dispute notification emails
