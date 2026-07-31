namespace RetailSuite.Api.MultiTenancy;

/// <summary>
/// Subdomain/slug values a tenant may never register — they'd collide with real
/// application routes or infrastructure hostnames (storefront path segments,
/// admin routes, and common infra labels reserved for future DNS use).
/// </summary>
public static class ReservedSubdomains
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "admin", "app", "shop", "store", "mail", "ftp"
    };

    public static bool IsReserved(string subdomain) =>
        !string.IsNullOrWhiteSpace(subdomain) && Reserved.Contains(subdomain.Trim());
}
