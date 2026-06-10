namespace RetailSuite.Infrastructure.Modules.Identity.Dtos;

public class SignupRequest
{
    public string TenantName { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Optional — billing notifications go here. Defaults to admin email if omitted.</summary>
    public string? BillingEmail { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code. Defaults to "PK".</summary>
    public string? CountryCode { get; set; }

    /// <summary>Subscription plan code to start on. Defaults to "FREE".</summary>
    public string? PlanCode { get; set; }

    /// <summary>Billing cycle for paid plans. "Monthly" or "Yearly". Defaults to "Monthly".</summary>
    public string? BillingCycle { get; set; }

    /// <summary>
    /// Optional card-on-file for auto-renewal of paid plans. Caller's responsibility to
    /// only send when a paid plan is selected. We capture display fields + an opaque
    /// gateway token; the full PAN never reaches the server in production (Stripe Elements
    /// returns a token; this DTO carries that token). In dev we accept the unmasked
    /// number, derive Last4 + Brand server-side, and treat it as a no-op gateway charge.
    /// </summary>
    public SignupPaymentMethod? PaymentMethod { get; set; }
}

public class SignupPaymentMethod
{
    /// <summary>"Card" or "BankTransfer". For now we only wire "Card".</summary>
    public string Type { get; set; } = "Card";

    /// <summary>Full card number — DEV ONLY. In production, send the gateway token instead.</summary>
    public string? CardNumber { get; set; }

    public string? HolderName { get; set; }
    public int     ExpMonth   { get; set; }
    public int     ExpYear    { get; set; }

    /// <summary>CVV — never persisted, used only to forward to gateway. DEV: ignored.</summary>
    public string? Cvv        { get; set; }

    /// <summary>If you've already created the card via Stripe Elements, pass the token here instead of raw card data.</summary>
    public string? GatewayToken { get; set; }
}
