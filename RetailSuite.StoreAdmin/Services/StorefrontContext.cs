/// <summary>
/// Scoped (per Blazor circuit) holder for the current storefront's tenant slug —
/// set from each storefront page's {TenantSlug} route parameter (see StorefrontPageBase),
/// and read reactively by CustomerLayout so header nav links point at the right store.
/// </summary>
public class StorefrontContext
{
    public string TenantSlug { get; private set; } = string.Empty;

    public event Action? OnChange;

    public void SetSlug(string slug)
    {
        var normalized = slug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized == TenantSlug) return;
        TenantSlug = normalized;
        OnChange?.Invoke();
    }
}
