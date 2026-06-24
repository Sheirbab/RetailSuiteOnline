using Ganss.Xss;

namespace RetailSuite.Infrastructure.Modules.Catalog.Services;

/// <summary>
/// Cleans user-supplied HTML (today: product descriptions) to a safe allow-list of tags
/// and attributes. Strips scripts, event handlers, inline styles that could leak data,
/// and disallowed schemes (<c>javascript:</c>, <c>data:</c> on links).
///
/// We trust the admin user — but we sanitise anyway so a future expansion to less-trusted
/// editors (e.g. vendor self-service onboarding) doesn't open an XSS surface.
/// </summary>
public interface IHtmlSanitizerService
{
    /// <summary>Returns sanitised HTML; null/empty input returns empty string.</summary>
    string Sanitize(string? html);
}

public class HtmlSanitizerService : IHtmlSanitizerService
{
    private readonly HtmlSanitizer _sanitizer;

    public HtmlSanitizerService()
    {
        _sanitizer = new HtmlSanitizer();

        // Default allow-list covers most formatting; keep it conservative.
        // The library already disallows <script>, event handler attrs, and disallows
        // javascript:/data: URIs. We start from its defaults and tighten/loosen lightly.

        // Allow common formatting + structure used by the product description editor.
        _sanitizer.AllowedTags.Clear();
        foreach (var t in new[]
        {
            "p", "br", "hr",
            "strong", "b", "em", "i", "u", "s",
            "h1", "h2", "h3", "h4", "h5", "h6",
            "ul", "ol", "li",
            "blockquote", "code", "pre",
            "a", "img",
            "table", "thead", "tbody", "tr", "th", "td",
            "span", "div"
        }) _sanitizer.AllowedTags.Add(t);

        _sanitizer.AllowedAttributes.Clear();
        foreach (var a in new[] { "href", "title", "alt", "src", "width", "height", "colspan", "rowspan" })
            _sanitizer.AllowedAttributes.Add(a);

        // Restrict link / image schemes.
        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("mailto");
    }

    public string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        return _sanitizer.Sanitize(html);
    }
}
