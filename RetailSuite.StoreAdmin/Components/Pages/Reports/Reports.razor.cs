using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace RetailSuite.StoreAdmin.Components.Pages.Reports;

/// <summary>
/// Code-behind for Reports.razor. Lives in a partial class file so we can use
/// generic methods like LoadAsync&lt;T&gt; without confusing Razor's HTML parser
/// (which mistakes &lt;T?&gt; for an unclosed tag inside @code).
/// </summary>
public partial class Reports : ComponentBase
{
    // Http / JS / Toast are injected via @inject in Reports.razor — the Razor
    // compiler generates [Inject] properties on the same partial class, so we
    // don't redeclare them here (that would be a duplicate-member error).

    private readonly string[] _tabs = new[]
    {
        "Overview", "Top products", "Low stock", "Payment mix",
        "Tax summary", "Categories", "Supplier dues", "P&L"
    };
    private string _activeTab = "Overview";
    private string _topBy     = "revenue";

    private DateTime From { get; set; } = DateTime.Today.AddDays(-30);
    private DateTime To   { get; set; } = DateTime.Today;

    private SalesReport?        _sales;
    private TopProductsReport?  _top;
    private LowStockReport?     _lowStock;
    private PaymentMixReport?   _paymentMix;
    private TaxSummaryReport?   _taxSummary;
    private CategoryReport?     _categories;
    private SupplierDuesReport? _supplierDues;
    private PlReport?           _pl;

    private string Range => $"from={From:yyyy-MM-dd}&to={To:yyyy-MM-dd}";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        await LoadAll();
    }

    private async Task SetTopBy(string by)
    {
        _topBy = by;
        _top = await LoadAsync<TopProductsReport>($"api/reports/top-products?{Range}&take=15&by={by}");
        StateHasChanged();
    }

    private async Task LoadAll()
    {
        _sales        = await LoadAsync<SalesReport>($"api/reports/sales?{Range}");
        _top          = await LoadAsync<TopProductsReport>($"api/reports/top-products?{Range}&take=15&by={_topBy}");
        _lowStock     = await LoadAsync<LowStockReport>("api/reports/low-stock");
        _paymentMix   = await LoadAsync<PaymentMixReport>($"api/reports/payment-mix?{Range}");
        _taxSummary   = await LoadAsync<TaxSummaryReport>($"api/reports/tax-summary?{Range}");
        _categories   = await LoadAsync<CategoryReport>($"api/reports/category-sales?{Range}");
        _supplierDues = await LoadAsync<SupplierDuesReport>("api/reports/supplier-dues");
        _pl           = await LoadAsync<PlReport>($"api/reports/pl?{Range}");
        StateHasChanged();
    }

    private async Task ExportTopProducts()
    {
        if (_top == null) return;
        var sb = new StringBuilder();
        sb.AppendLine("Rank,Product,Units,Revenue,Cost,Gross profit");
        var idx = 0;
        foreach (var r in _top.Items)
        {
            idx++;
            sb.AppendLine($"{idx},\"{r.ProductName.Replace("\"", "\"\"")}\",{r.UnitsSold},{r.Revenue},{r.Cost},{r.GrossProfit}");
        }
        await JS.InvokeVoidAsync("downloadCsv", $"top-products-{From:yyyyMMdd}-{To:yyyyMMdd}.csv", sb.ToString());
        Toast.Show("CSV downloaded.");
    }

    private async Task<T?> LoadAsync<T>(string url) where T : class
    {
        try
        {
            var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            return JsonSerializer.Deserialize<T>(data.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    // -----------------------------------------------------------------
    // DTOs — match the shape returned by ReportsController
    // -----------------------------------------------------------------
    public record SalesReport
    {
        public int      TotalOrders       { get; init; }
        public decimal  TotalRevenue      { get; init; }
        public decimal  TotalTax          { get; init; }
        public decimal  AverageOrderValue { get; init; }
        public List<DailyPoint> DailyBreakdown { get; init; } = new();
    }
    public record DailyPoint(string Date, int Count, decimal Revenue, decimal Tax);

    public record TopProductsReport(List<TopProductRow> Items);
    public record TopProductRow(string ProductName, int UnitsSold, decimal Revenue, decimal Cost, decimal GrossProfit);

    public record LowStockReport(int Count, List<LowStockRow> Items);
    public record LowStockRow(string Sku, string ProductName, int CurrentStock, int Threshold, int Shortfall);

    public record PaymentMixReport(List<PaymentMixRow> Mix);
    public record PaymentMixRow(string Method, int Count, decimal Total, double Share);

    public record TaxSummaryReport(decimal NetSales, decimal TotalTax, List<TaxRateRow> ByTaxRate);
    public record TaxRateRow(decimal Rate, string RatePct, decimal NetSales, decimal TaxDue);

    public record CategoryReport(List<CategoryRow> Categories);
    public record CategoryRow(string CategoryName, int UnitsSold, decimal Revenue);

    public record SupplierDuesReport(List<SupplierDueRow> Suppliers);
    public record SupplierDueRow(string SupplierName, int OrderCount, decimal ReceivedTotal);

    public record PlReport(decimal Revenue, decimal COGS, decimal GrossProfit, decimal TaxPayable, decimal NetProfit);
}
