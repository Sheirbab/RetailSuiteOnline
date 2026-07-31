using Microsoft.AspNetCore.Components;

namespace RetailSuite.StoreAdmin.Components.Pages.Shop;

/// <summary>
/// Base class for every public storefront page — each is routed under
/// /store/{TenantSlug}/... . Resolves the tenant slug into the shared
/// <see cref="StorefrontContext"/> (read by CustomerLayout for nav links) and keeps
/// the cart scoped to whichever tenant's store the shopper is currently on.
/// </summary>
public abstract class StorefrontPageBase : ComponentBase
{
    [Parameter] public string TenantSlug { get; set; } = string.Empty;

    [Inject] protected StorefrontContext Store { get; set; } = default!;
    [Inject] protected CartService Cart { get; set; } = default!;

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (!string.IsNullOrWhiteSpace(TenantSlug))
        {
            Store.SetSlug(TenantSlug);
            Cart.EnsureTenant(TenantSlug);
        }
    }

    /// <summary>Build a tenant-scoped storefront API path, e.g. "products" -> "api/shop/demo-store/products".</summary>
    protected string ShopApi(string relativePath) => $"api/shop/{TenantSlug}/{relativePath.TrimStart('/')}";
}
