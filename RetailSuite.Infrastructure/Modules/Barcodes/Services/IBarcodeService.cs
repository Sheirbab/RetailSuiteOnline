namespace RetailSuite.Infrastructure.Modules.Barcodes.Services;

/// <summary>
/// Generates barcode + QR code PNG images.
/// Implementation uses ZXing.Net for encoding and SkiaSharp for rendering — both
/// cross-platform (no Windows-only System.Drawing dependency).
/// </summary>
public interface IBarcodeService
{
    /// <summary>
    /// Generate a Code128 barcode PNG for the given value.
    /// </summary>
    /// <param name="value">SKU or arbitrary printable ASCII to encode.</param>
    /// <param name="widthPx">Final image width in pixels. Includes quiet zone padding.</param>
    /// <param name="heightPx">Final image height in pixels.</param>
    /// <param name="includeText">If true, the value is drawn below the bars.</param>
    byte[] GenerateCode128Png(string value, int widthPx = 400, int heightPx = 120, bool includeText = true);

    /// <summary>
    /// Generate a QR code PNG for the given value. Square aspect.
    /// </summary>
    byte[] GenerateQrPng(string value, int sizePx = 300);
}
