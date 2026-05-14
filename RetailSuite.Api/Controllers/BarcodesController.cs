using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Barcodes.Services;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Barcode generation + printable label sheets.
/// Returns raw PNG for single barcodes and a print-ready HTML page for label sheets —
/// users click their browser's Print and the page lays out at the correct mm size for
/// thermal or A4 printers.
/// </summary>
[ApiController]
[Route("api/barcodes")]
[Authorize(Policy = "StaffOrAdmin")]
public class BarcodesController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly IBarcodeService _barcodes;

    public BarcodesController(RetailDbContext db, IBarcodeService barcodes)
    {
        _db = db;
        _barcodes = barcodes;
    }

    // -------------------------------------------------------------
    // GET /api/barcodes/{variantId}.png
    // -------------------------------------------------------------
    /// <summary>Single Code128 barcode PNG for the variant's Barcode (or SKU if blank).</summary>
    [HttpGet("{variantId:guid}.png")]
    public async Task<IActionResult> GetBarcodePng(
        Guid variantId,
        [FromQuery] int width  = 400,
        [FromQuery] int height = 120,
        [FromQuery] bool text  = true)
    {
        var value = await ResolveBarcodeValueAsync(variantId);
        if (value == null)
            return NotFound();

        var png = _barcodes.GenerateCode128Png(value, width, height, text);
        return File(png, "image/png");
    }

    // -------------------------------------------------------------
    // GET /api/barcodes/qr/{variantId}.png
    // -------------------------------------------------------------
    /// <summary>QR-code PNG for the variant's Barcode (or SKU).</summary>
    [HttpGet("qr/{variantId:guid}.png")]
    public async Task<IActionResult> GetQrPng(Guid variantId, [FromQuery] int size = 300)
    {
        var value = await ResolveBarcodeValueAsync(variantId);
        if (value == null)
            return NotFound();

        var png = _barcodes.GenerateQrPng(value, size);
        return File(png, "image/png");
    }

    // -------------------------------------------------------------
    // GET /api/barcodes/print
    // -------------------------------------------------------------
    /// <summary>
    /// Printable label sheet. Supported <c>layout</c> values:
    ///   <list type="bullet">
    ///     <item><c>A4-3x8</c> — A4 sheet with 24 labels (3 cols × 8 rows). 70×35 mm each.</item>
    ///     <item><c>A4-2x7</c> — A4 sheet with 14 larger labels (2 cols × 7 rows). 99×38 mm each.</item>
    ///     <item><c>thermal-50x30</c> — single 50×30 mm thermal label per page.</item>
    ///     <item><c>thermal-100x50</c> — single 100×50 mm thermal label per page (shipping size).</item>
    ///   </list>
    /// <c>variantIds</c> is comma-separated GUIDs; <c>copies</c> repeats each variant N times.
    /// </summary>
    [HttpGet("print")]
    [Produces("text/html")]
    public async Task<IActionResult> PrintLabels(
        [FromQuery] string variantIds,
        [FromQuery] int copies   = 1,
        [FromQuery] string layout = "A4-3x8")
    {
        if (string.IsNullOrWhiteSpace(variantIds))
            return BadRequest("variantIds is required (comma-separated guids).");

        copies = Math.Clamp(copies, 1, 100);

        var ids = variantIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue).Select(g => g!.Value)
            .ToList();

        if (ids.Count == 0)
            return BadRequest("No valid variantIds.");

        var variants = await _db.ProductVariants
            .AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .Select(v => new LabelRow
            {
                VariantId = v.Id,
                Sku       = v.SKU,
                Barcode   = string.IsNullOrWhiteSpace(v.Barcode) ? v.SKU : v.Barcode,
                Price     = v.Price
            })
            .ToListAsync();

        if (variants.Count == 0)
            return NotFound("No variants matched the supplied ids.");

        // Expand by copies, preserving input order.
        var rows = new List<LabelRow>(variants.Count * copies);
        foreach (var id in ids)
        {
            var match = variants.FirstOrDefault(v => v.VariantId == id);
            if (match == null) continue;
            for (int i = 0; i < copies; i++) rows.Add(match);
        }

        var html = RenderHtml(rows, layout);
        return Content(html, "text/html", Encoding.UTF8);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private async Task<string?> ResolveBarcodeValueAsync(Guid variantId)
    {
        var row = await _db.ProductVariants
            .AsNoTracking()
            .Where(v => v.Id == variantId)
            .Select(v => new { v.SKU, v.Barcode })
            .FirstOrDefaultAsync();
        if (row == null) return null;
        return string.IsNullOrWhiteSpace(row.Barcode) ? row.SKU : row.Barcode;
    }

    private static string RenderHtml(List<LabelRow> rows, string layout)
    {
        // Layout descriptors. Page size + label size in millimetres; CSS @page handles the rest.
        var spec = layout.ToLowerInvariant() switch
        {
            "a4-3x8"        => new LayoutSpec("210mm 297mm", 70, 35,  3, 8,  60, 80,   28, 70),
            "a4-2x7"        => new LayoutSpec("210mm 297mm", 99, 38,  2, 7,  85, 90,   32, 80),
            "thermal-50x30" => new LayoutSpec("50mm 30mm",   50, 30,  1, 1,  45, 70,   24, 50),
            "thermal-100x50"=> new LayoutSpec("100mm 50mm", 100, 50,  1, 1,  90, 90,   32, 80),
            _ => throw new ArgumentException($"Unknown layout '{layout}'.")
        };

        var sb = new StringBuilder();
        sb.Append("""
        <!doctype html><html><head><meta charset="utf-8"><title>Print labels</title>
        <style>
          html, body { margin: 0; padding: 0; background: #fff; color: #000; font-family: Arial, sans-serif; }
        """);

        sb.AppendFormat(CultureInfo.InvariantCulture, "@page {{ size: {0}; margin: 5mm; }} ", spec.PageSize);

        // Grid layout (1×1 for thermal, NxM for A4).
        sb.AppendFormat(CultureInfo.InvariantCulture,
            ".sheet {{ display: grid; grid-template-columns: repeat({0}, 1fr); gap: 2mm; page-break-after: always; }} ",
            spec.Cols);

        sb.AppendFormat(CultureInfo.InvariantCulture,
            ".label {{ width: {0}mm; height: {1}mm; box-sizing: border-box; border: 0.2mm solid #ddd; padding: 1mm; display: flex; flex-direction: column; align-items: center; justify-content: center; overflow: hidden; }} ",
            spec.LabelW, spec.LabelH);

        // width in mm, height auto preserves the PNG aspect ratio (~10:3 for Code128).
        sb.AppendFormat(CultureInfo.InvariantCulture,
            ".barcode {{ width: {0}mm; height: auto; max-height: {1}mm; }} ",
            spec.BarcodeMmWidth, Math.Max(8, spec.LabelH / 2));

        sb.AppendFormat(CultureInfo.InvariantCulture,
            ".sku   {{ font-size: 8pt; font-family: 'Courier New', monospace; letter-spacing: 0.5px; }} ");

        sb.AppendFormat(CultureInfo.InvariantCulture,
            ".price {{ font-size: 10pt; font-weight: bold; margin-top: 1mm; }} ");

        sb.Append("</style></head><body>");

        // Chunk rows into pages of (Cols * Rows) for paginated layouts.
        var perPage = spec.Cols * spec.Rows;
        for (int i = 0; i < rows.Count; i += perPage)
        {
            sb.Append("<div class=\"sheet\">");
            for (int j = i; j < Math.Min(rows.Count, i + perPage); j++)
            {
                var r = rows[j];
                sb.Append("<div class=\"label\">");
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<img class=\"barcode\" alt=\"{0}\" src=\"/api/barcodes/{1}.png?width={2}&height={3}&text=false\" />",
                    EscapeHtml(r.Sku), r.VariantId, spec.BarcodePngWidth, spec.BarcodePngHeight);
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<div class=\"sku\">{0}</div>", EscapeHtml(r.Sku));
                if (r.Price > 0)
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "<div class=\"price\">PKR {0:N0}</div>", r.Price);
                sb.Append("</div>");
            }
            sb.Append("</div>");
        }

        sb.Append("<script>window.addEventListener('load', () => setTimeout(() => window.print(), 400));</script>");
        sb.Append("</body></html>");

        return sb.ToString();
    }

    private static string EscapeHtml(string s) =>
        System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

    private sealed class LabelRow
    {
        public Guid VariantId { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }

    /// <summary>Layout dimensions (page + label + barcode sizing).</summary>
    private sealed record LayoutSpec(
        string PageSize,
        int LabelW, int LabelH,
        int Cols, int Rows,
        int BarcodeMmWidth, int BarcodeHeightPx,
        int BarcodePngWidth, int BarcodePngHeight);
}
