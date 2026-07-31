using Microsoft.AspNetCore.Components;

namespace RetailSuite.StoreAdmin.Components.Pages;

/// <summary>
/// Base class for every authenticated admin page — each is routed under
/// /{TenantSlug}/admin/... purely for URL consistency with the public storefront
/// (/store/{TenantSlug}/...). Unlike the storefront, admin data scoping is already
/// fully handled by the JWT's tenantId claim — TenantSlug here is not used for
/// authorization or API calls, only so the route matches.
/// </summary>
public abstract class AdminPageBase : ComponentBase
{
    [Parameter] public string TenantSlug { get; set; } = string.Empty;
}
