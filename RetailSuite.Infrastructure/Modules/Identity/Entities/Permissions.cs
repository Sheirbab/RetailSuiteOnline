namespace RetailSuite.Infrastructure.Modules.Identity.Entities;

/// <summary>
/// Catalogue of permission codes. Each code maps roughly to one screen / surface area
/// in the admin or POS UI. Tenant admin assigns these per user. Codes are intentionally
/// short string constants (not enums) so adding a new permission is a one-line change
/// without a migration.
/// </summary>
public static class Permissions
{
    // ---- POS / sales ----
    public const string Pos              = "POS";              // Use the POS terminal, complete sales
    public const string PosHoldResume    = "POS_HOLD";         // Park / resume sales
    public const string PosReturn        = "POS_RETURN";       // Process returns at the till
    public const string PosEodReports    = "POS_EOD";          // X / Z cash reports

    // ---- Inventory ----
    public const string InventoryView    = "INVENTORY_VIEW";
    public const string InventoryAdjust  = "INVENTORY_ADJUST"; // Manual stock adjustments
    public const string InventoryTransfer = "INVENTORY_TRANSFER";

    // ---- Catalog ----
    public const string Products         = "PRODUCTS";
    public const string Categories       = "CATEGORIES";
    public const string Barcodes         = "BARCODES";

    // ---- Orders ----
    public const string OrdersView       = "ORDERS_VIEW";
    public const string OrdersFulfil     = "ORDERS_FULFIL";    // Mark shipped / delivered

    // ---- Customers ----
    public const string Customers        = "CUSTOMERS";
    public const string StoreCredit      = "STORE_CREDIT";
    public const string LoyaltySettings  = "LOYALTY_SETTINGS";

    // ---- Procurement ----
    public const string Suppliers        = "SUPPLIERS";
    public const string ReceivingOrders  = "RECEIVING_ORDERS";
    public const string BulkReceive      = "BULK_RECEIVE";
    public const string SupplierReturns  = "SUPPLIER_RETURNS";

    // ---- Reports / accounting ----
    public const string Reports          = "REPORTS";
    public const string Accounting       = "ACCOUNTING";

    // ---- Settings (tenant-wide config) ----
    public const string Locations        = "LOCATIONS";
    public const string ShippingMethods  = "SHIPPING";
    public const string TaxSettings      = "TAX_SETTINGS";
    public const string Subscription     = "SUBSCRIPTION";
    public const string UserManagement   = "USERS";            // Manage other users

    /// <summary>
    /// All permission codes in a UI-friendly grouped structure for the user-edit page.
    /// </summary>
    public static readonly PermissionGroup[] Catalog = new[]
    {
        new PermissionGroup("POS / Sales", new[]
        {
            new PermissionEntry(Pos,             "Use POS terminal"),
            new PermissionEntry(PosHoldResume,   "Hold / resume sales"),
            new PermissionEntry(PosReturn,       "Process returns"),
            new PermissionEntry(PosEodReports,   "X / Z cash reports"),
        }),
        new PermissionGroup("Inventory", new[]
        {
            new PermissionEntry(InventoryView,     "View inventory"),
            new PermissionEntry(InventoryAdjust,   "Adjust stock manually"),
            new PermissionEntry(InventoryTransfer, "Inter-branch transfers"),
        }),
        new PermissionGroup("Catalog", new[]
        {
            new PermissionEntry(Products,   "Products"),
            new PermissionEntry(Categories, "Categories"),
            new PermissionEntry(Barcodes,   "Print barcodes"),
        }),
        new PermissionGroup("Orders", new[]
        {
            new PermissionEntry(OrdersView,   "View orders"),
            new PermissionEntry(OrdersFulfil, "Mark shipped / delivered"),
        }),
        new PermissionGroup("Customers", new[]
        {
            new PermissionEntry(Customers,        "Customer records"),
            new PermissionEntry(StoreCredit,      "Issue / view store credit"),
            new PermissionEntry(LoyaltySettings,  "Loyalty settings"),
        }),
        new PermissionGroup("Procurement", new[]
        {
            new PermissionEntry(Suppliers,        "Suppliers"),
            new PermissionEntry(ReceivingOrders,  "Receiving orders"),
            new PermissionEntry(BulkReceive,      "Bulk receive"),
            new PermissionEntry(SupplierReturns,  "Supplier returns"),
        }),
        new PermissionGroup("Reports / Accounting", new[]
        {
            new PermissionEntry(Reports,    "Sales / inventory reports"),
            new PermissionEntry(Accounting, "GL / journal entries"),
        }),
        new PermissionGroup("Settings", new[]
        {
            new PermissionEntry(Locations,        "Locations"),
            new PermissionEntry(ShippingMethods,  "Shipping methods"),
            new PermissionEntry(TaxSettings,      "Tax & FBR"),
            new PermissionEntry(Subscription,     "Subscription"),
            new PermissionEntry(UserManagement,   "User management"),
        }),
    };

    /// <summary>Returns true if the code is one of the known permissions in the catalog.</summary>
    public static bool IsKnown(string code) =>
        Catalog.SelectMany(g => g.Entries).Any(e => e.Code == code);
}

public record PermissionEntry(string Code, string Label);
public record PermissionGroup(string Title, PermissionEntry[] Entries);
