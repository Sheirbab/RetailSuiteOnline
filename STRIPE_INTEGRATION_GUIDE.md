# 🔐 Stripe Integration Guide

**Phase 2 Priority 2**: Production Payment Processing  
**Status**: ✅ **IMPLEMENTED & TESTED**

---

## 📦 What Was Implemented

### 1. **Stripe Payment Gateway** ✅
- Charge processing with Stripe API
- Full and partial refunds
- Comprehensive error handling
- Structured logging for debugging

### 2. **Webhook Handler** ✅
- Process Stripe events: charge.succeeded, charge.failed, charge.refunded
- Signature verification for security
- Support for chargebacks/disputes
- Extensible event handling

### 3. **Configuration** ✅
- Environment-aware payment gateway selection
  - **Dev**: FakePaymentGateway (mock, always succeeds)
  - **Prod**: StripePaymentGateway (real transactions)
- Secure API key management
- Webhook endpoint protected with Stripe signature

### 4. **API Endpoints** ✅
- `POST /api/webhooks/stripe` - Webhook receiver
- Signature verification built-in
- Automatic event routing

---

## 🚀 Getting Started

### Prerequisites
1. **Stripe Account**: https://stripe.com (free account available)
2. **API Keys**: Get from Stripe Dashboard > Settings > API Keys
3. **.NET 8 Project**: Already configured ✅

### Step 1: Get Stripe API Keys

1. Go to **Stripe Dashboard** (https://dashboard.stripe.com)
2. Navigate to **Settings** > **Developers** > **API keys**
3. You'll see:
   - **Publishable key**: `pk_test_...` (safe for client-side)
   - **Secret key**: `sk_test_...` (NEVER expose publicly)

### Step 2: Configure API Keys

**Option A: Local Development (appsettings.Development.json)**
```json
{
  "Stripe": {
    "PublishableKey": "pk_test_YOUR_KEY_HERE",
    "SecretKey": "sk_test_YOUR_KEY_HERE",
    "WebhookSecret": ""
  }
}
```

**Option B: Environment Variables (Recommended for Production)**
```bash
# Linux/Mac
export STRIPE_API_KEY="sk_test_YOUR_KEY_HERE"
export STRIPE_WEBHOOK_SECRET="whsec_YOUR_WEBHOOK_SECRET"

# Windows PowerShell
$env:STRIPE_API_KEY = "sk_test_YOUR_KEY_HERE"
$env:STRIPE_WEBHOOK_SECRET = "whsec_YOUR_WEBHOOK_SECRET"
```

**Option C: User Secrets (Recommended for Local Dev)**
```bash
dotnet user-secrets set "Stripe:SecretKey" "sk_test_YOUR_KEY_HERE"
dotnet user-secrets set "Stripe:WebhookSecret" "whsec_YOUR_WEBHOOK_SECRET"
```

### Step 3: Set Up Webhooks

1. In **Stripe Dashboard**, go to **Developers** > **Webhooks**
2. Click **Add an endpoint**
3. Enter your webhook URL:
   ```
   https://your-api-domain.com/api/webhooks/stripe
   ```
4. For **local testing**, use **Stripe CLI** to forward webhooks to your dev machine

### Step 4: Test Locally with Stripe CLI

```bash
# Install Stripe CLI: https://stripe.com/docs/stripe-cli

# Login to your account
stripe login

# Listen for webhook events and forward to local API
stripe listen --forward-to localhost:5000/api/webhooks/stripe

# You'll get a webhook signing secret
# Copy it to appsettings.json under Stripe:WebhookSecret
```

### Step 5: Trigger Test Events

```bash
# In another terminal, trigger test payment events
stripe trigger charge.succeeded
stripe trigger charge.failed
stripe trigger charge.refunded

# Watch your API logs for webhook processing
```

---

## 💰 Usage in Code

### Process a Payment

```csharp
// Inject IPaymentGateway
var paymentGateway = serviceProvider.GetRequiredService<IPaymentGateway>();

// Process charge
var result = await paymentGateway.ChargeAsync(
    amount: 150.00m,
    currency: "USD",
    reference: "ORD-12345"  // Order ID
);

if (result.Success)
{
    // Save transaction ID for refunds
    var transactionId = result.TransactionId; // "ch_1234..."
    await orderService.RecordPaymentAsync(orderId, transactionId);
}
else
{
    // Handle payment failure
    var error = result.Error;
    await notificationService.SendPaymentFailedEmailAsync(order, error);
}
```

### Process a Refund

```csharp
// Refund a previous charge
var refundResult = await paymentGateway.RefundAsync(
    transactionId: "ch_1234...",
    amount: 50.00m  // Partial refund (0 = full refund)
);

if (refundResult.Success)
{
    var refundId = refundResult.TransactionId;
    await orderService.RecordRefundAsync(orderId, refundId);
}
```

### Handle Webhooks

```csharp
// Webhooks are automatically routed by WebhookController
// The StripeWebhookHandler processes events:

// charge.succeeded → Order marked as paid
// charge.failed → Order payment status set to failed
// charge.refunded → Refund recorded in database
// charge.dispute.created → Admin alert triggered
```

---

## 📊 Architecture

```
┌─────────────────────────────────────────────────────┐
│              Client (Blazor Admin)                   │
│         (Displays payment status)                    │
└──────────────────┬──────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────┐
│          RetailSuite.Api (ASP.NET Core)              │
│                                                      │
│  ┌─────────────────────────────────────────────┐   │
│  │  PaymentController / OrdersController       │   │
│  │  (Initiate payment charge)                  │   │
│  └──────────────────┬──────────────────────────┘   │
│                     │                               │
│  ┌──────────────────▼──────────────────────────┐   │
│  │  IPaymentGateway (Interface)                │   │
│  └──────────────────┬──────────────────────────┘   │
│                     │                               │
│      ┌──────────────┴──────────────┐              │
│      │                             │              │
│  (Dev)                        (Production)        │
│      ▼                             ▼              │
│  ┌─────────────────┐      ┌──────────────────┐  │
│  │  FakePayment    │      │ StripePayment    │  │
│  │  Gateway        │      │ Gateway          │  │
│  │  (Mock)         │      │ (Real)           │  │
│  └─────────────────┘      └────────┬─────────┘  │
│                                    │             │
│  ┌────────────────────────────────▼────────┐    │
│  │  WebhookController                      │    │
│  │  POST /api/webhooks/stripe              │    │
│  │  (Receives Stripe events)               │    │
│  └────────────────────┬─────────────────────┘   │
│                       │                          │
│  ┌────────────────────▼─────────────────────┐   │
│  │  StripeWebhookHandler                    │   │
│  │  (Routes events to handlers)             │   │
│  │  - charge.succeeded                      │   │
│  │  - charge.failed                         │   │
│  │  - charge.refunded                       │   │
│  │  - charge.dispute.created                │   │
│  └────────────────────┬─────────────────────┘   │
│                       │                          │
│  ┌────────────────────▼──────────────────────┐  │
│  │  Order & Payment Database                 │  │
│  │  (Update payment status)                  │  │
│  └─────────────────────────────────────────┬─┘  │
└────────────────────────────────────────────┼────┘
                                             │
                    ┌────────────────────────▼─────┐
                    │     Stripe API                │
                    │     (Process charges)        │
                    │     (Send webhooks)          │
                    └─────────────────────────────┘
```

---

## 🔒 Security Considerations

### ✅ Implemented
- **Webhook Signature Verification**: All webhooks verified with Stripe secret
- **Secret Key Protection**: Never exposed in logs or client-side code
- **Error Messages**: Don't leak sensitive payment details
- **Logging**: Comprehensive logging without storing sensitive data

### 🔐 Best Practices

1. **Never Commit API Keys**
   ```bash
   # Use .gitignore to exclude secrets
   echo "appsettings.Development.json" >> .gitignore
   ```

2. **Rotate Keys Regularly**
   - Stripe allows multiple API keys
   - Rotate old keys quarterly

3. **Use Restricted Keys**
   - Create Stripe API keys with minimal permissions
   - Separate keys for read-only vs. write operations

4. **Environment Separation**
   - Use `pk_test_` and `sk_test_` keys for development
   - Use `pk_live_` and `sk_live_` keys for production
   - Never mix test and live keys

5. **Webhook Secret Management**
   - Store webhook secret securely
   - Rotate if compromised
   - Don't share with frontend developers

---

## 📝 Configuration Checklist

### Development Setup
- [ ] Create Stripe test account
- [ ] Get test API keys (pk_test_, sk_test_)
- [ ] Add keys to appsettings.Development.json
- [ ] Install Stripe CLI
- [ ] Run `stripe listen` for local webhook testing
- [ ] Test charge/refund operations locally

### Production Setup
- [ ] Upgrade Stripe account to production
- [ ] Get live API keys (pk_live_, sk_live_)
- [ ] Store in environment variables (NOT in config files)
- [ ] Register webhook endpoint URL in Stripe Dashboard
- [ ] Get webhook signing secret
- [ ] Test end-to-end payment flow
- [ ] Enable retry policies for failed webhooks

---

## 🧪 Testing Payment Flows

### Test Cards (Stripe Provides)

```
Visa Card:              4242 4242 4242 4242
Visa Card (Decline):    4000 0000 0000 0002
Mastercard:             5555 5555 5555 4444
American Express:       3782 822463 10005
```

**All use any future expiry and any 3-digit CVC**

### Test Payment Flow

```bash
# 1. Start API
cd RetailSuite.Api
dotnet run

# 2. Start webhook listener (in another terminal)
stripe listen --forward-to localhost:5000/api/webhooks/stripe
# Copy: whsec_... (webhook secret)

# 3. Add to appsettings.Development.json
# "WebhookSecret": "whsec_..."

# 4. Make a payment request
curl -X POST https://localhost:7000/api/payments/charge \
  -H "Authorization: Bearer YOUR_JWT" \
  -H "Content-Type: application/json" \
  -d '{"orderId":"550e8400-e29b-41d4-a716-446655440000","amount":150}'

# 5. Simulate webhook events
stripe trigger charge.succeeded

# 6. Check API logs for webhook processing
```

---

## 🚨 Common Issues & Solutions

### Issue: "Webhook secret not configured"
**Solution**: Add WebhookSecret to appsettings.json or environment variables

### Issue: "Invalid API key"
**Solution**: Ensure SecretKey starts with `sk_test_` or `sk_live_`

### Issue: "Signature verification failed"
**Solution**: Webhook secret doesn't match Stripe Dashboard. Regenerate in Stripe.

### Issue: "Amount must be positive"
**Solution**: Stripe expects amount in cents. $150 = 15000 cents (handled in code)

### Issue: Webhook not being delivered
**Solution**: 
- Check webhook URL is public and accessible
- Verify Stripe can reach your server (firewall rules)
- Check retry policy in Stripe Dashboard

---

## 📊 Monitoring & Logging

### View Logs
```bash
# Watch API logs for payment operations
tail -f logs/retailsuite-2025-*.log | grep -i "stripe\|payment"
```

### Log Examples
```
[INF] Processing Stripe charge: Amount=150.00 USD, Reference=ORD-12345
[INF] Stripe charge succeeded: ChargeId=ch_1234..., Amount=150.00 USD
[INF] Processing Stripe webhook event: Type=charge.succeeded, Id=evt_1234...
[WRN] Stripe charge failed: Status=declined, Reference=ORD-12345
[ERR] Stripe API error: Code=card_error, Message=Your card was declined
```

### Stripe Dashboard Monitoring
- Logs: **Developers** > **Webhooks** > click endpoint > Recent events
- Payments: **Payments** > view transaction history
- Reports: **Reports** > Payments, Payouts, Disputes

---

## 🎯 Next Steps

### Immediate
- [ ] Configure Stripe API keys
- [ ] Test with Stripe CLI locally
- [ ] Verify webhook processing

### Short Term
- [ ] Update PaymentController to accept payment method tokens
- [ ] Implement Stripe Elements on frontend (Blazor component)
- [ ] Add customer payment method management UI
- [ ] Enable saved payment methods for recurring charges

### Later
- [ ] Setup SCA/3D Secure for enhanced security
- [ ] Implement invoice generation for payments
- [ ] Add payment receipts via email
- [ ] Connect to accounting system for reconciliation
- [ ] Setup Stripe Radar for fraud detection

---

## 📞 Support Resources

- **Stripe Docs**: https://stripe.com/docs
- **Stripe CLI**: https://stripe.com/docs/stripe-cli
- **API Reference**: https://stripe.com/docs/api
- **Webhook Events**: https://stripe.com/docs/webhooks
- **Test Mode**: https://stripe.com/docs/testing

---

## ✅ Implementation Status

```
✅ Stripe.net SDK integrated (v51.1.0)
✅ StripePaymentGateway implementation
✅ Webhook handler with signature verification
✅ Environment-aware registration (Dev/Prod)
✅ Configuration management
✅ Comprehensive logging
✅ All unit tests passing
✅ Ready for configuration with real keys
```

**Next Phase**: Update frontend to use Stripe payment methods and add customer payment method management.

---

**Created**: January 15, 2025  
**Status**: ✅ Implementation Complete, Ready for Configuration
