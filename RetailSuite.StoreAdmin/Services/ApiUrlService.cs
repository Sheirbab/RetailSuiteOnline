/// <summary>
/// Resolves API-hosted asset URLs (uploaded product images, payment QR codes, etc.)
/// against the configured API base URL.
///
/// The storefront and the API run on different origins — uploaded files live under
/// the API's wwwroot/uploads, so an `<img src="/uploads/...">` on the StoreAdmin page
/// would 404 (the browser would request StoreAdmin's host). This helper prefixes the
/// configured API base so the image points at the right server.
/// </summary>
public class ApiUrlService
{
    private readonly string _apiBaseUrl;

    public ApiUrlService(IConfiguration config)
    {
        var raw = config["Api:BaseUrl"] ?? "https://localhost:7001/";
        // Strip the trailing slash so concatenation is simple.
        _apiBaseUrl = raw.TrimEnd('/');
    }

    /// <summary>API base URL with no trailing slash (e.g. "https://localhost:7001").</summary>
    public string BaseUrl => _apiBaseUrl;

    /// <summary>
    /// Convert a possibly-relative URL into an absolute URL pointing at the API host.
    /// - null / empty → null
    /// - absolute (http(s)://…) → returned unchanged
    /// - relative starting with "/" → ApiBaseUrl + path
    /// - relative without leading "/" → ApiBaseUrl + "/" + path
    /// </summary>
    public string? Absolute(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;

        return url.StartsWith("/")
            ? _apiBaseUrl + url
            : _apiBaseUrl + "/" + url;
    }
}
