namespace RetailSuite.Infrastructure.Payments;

/// <summary>
/// Stripe configuration options.
/// Bind from appsettings.json under "Stripe" section.
/// </summary>
public class StripeOptions
{
    public const string Section = "Stripe";

    /// <summary>Stripe public API key (publishable key).</summary>
    /// <remarks>Used on client-side for Stripe Elements.</remarks>
    public string? PublishableKey { get; set; }

    /// <summary>Stripe secret API key.</summary>
    /// <remarks>NEVER expose this in client code. Keep server-side only.</remarks>
    public string? SecretKey { get; set; }

    /// <summary>Webhook signing secret for verifying webhook authenticity.</summary>
    /// <remarks>Used to verify that webhooks actually came from Stripe.</remarks>
    public string? WebhookSecret { get; set; }

    /// <summary>Validate that required keys are configured.</summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(SecretKey);

    /// <summary>Validate that webhook is properly configured.</summary>
    public bool IsWebhookConfigured => !string.IsNullOrWhiteSpace(WebhookSecret);
}
