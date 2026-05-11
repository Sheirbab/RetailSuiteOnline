namespace RetailSuite.Infrastructure.Payments;

/// <summary>
/// Top-level payment configuration. Bind from appsettings.json "Payments" section.
/// </summary>
public class PaymentOptions
{
    public const string Section = "Payments";

    /// <summary>
    /// Active payment provider. Recognised values (case-insensitive):
    /// "Stripe", "EasyPaisa", "JazzCash", "Cash", "Fake".
    /// Default = "Fake" so dev/test environments work out of the box.
    /// </summary>
    public string Provider { get; set; } = "Fake";
}

/// <summary>
/// Strongly-typed identifiers for supported payment providers.
/// Keeps switch statements and tests readable.
/// </summary>
public static class PaymentProviders
{
    public const string Stripe    = "Stripe";
    public const string EasyPaisa = "EasyPaisa";
    public const string JazzCash  = "JazzCash";
    public const string Cash      = "Cash";
    public const string Fake      = "Fake";
}
