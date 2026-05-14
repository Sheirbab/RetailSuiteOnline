using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace RetailSuite.Infrastructure.Modules.Barcodes.Services;

/// <summary>
/// ZXing.Net + SkiaSharp implementation of <see cref="IBarcodeService"/>.
/// ZXing emits a <see cref="BitMatrix"/> of black/white cells; SkiaSharp renders that
/// matrix into a PNG, optionally with a human-readable line of text below.
/// </summary>
public class BarcodeService : IBarcodeService
{
    public byte[] GenerateCode128Png(string value, int widthPx = 400, int heightPx = 120, bool includeText = true)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Barcode value must not be empty.", nameof(value));

        // Reserve ~22px at the bottom for the human-readable text strip.
        var textStrip = includeText ? 22 : 0;
        var barsHeight = Math.Max(20, heightPx - textStrip);

        var writer = new BarcodeWriterGeneric
        {
            Format  = BarcodeFormat.CODE_128,
            Options = new EncodingOptions
            {
                Width   = widthPx,
                Height  = barsHeight,
                Margin  = 10
            }
        };

        var matrix = writer.Encode(value);
        return RenderMatrixToPng(matrix, widthPx, heightPx, includeText ? value : null);
    }

    public byte[] GenerateQrPng(string value, int sizePx = 300)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("QR value must not be empty.", nameof(value));

        var writer = new BarcodeWriterGeneric
        {
            Format  = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                Width   = sizePx,
                Height  = sizePx,
                Margin  = 1
            }
        };

        var matrix = writer.Encode(value);
        return RenderMatrixToPng(matrix, sizePx, sizePx, humanText: null);
    }

    /// <summary>
    /// Render a ZXing BitMatrix to a PNG byte array. If <paramref name="humanText"/>
    /// is provided, it is drawn under the bars in a small monospace font.
    /// </summary>
    private static byte[] RenderMatrixToPng(BitMatrix matrix, int width, int height, string? humanText)
    {
        // ZXing's Width/Height may differ slightly from requested if format constraints round.
        var mWidth  = matrix.Width;
        var mHeight = matrix.Height;

        // Reserve room for human-readable text if requested.
        var textHeight = humanText is null ? 0 : 22;
        var canvasW    = mWidth;
        var canvasH    = mHeight + textHeight;

        using var bitmap = new SKBitmap(canvasW, canvasH, isOpaque: true);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.White);

        // Paint black cells.
        using var blackPaint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        for (int y = 0; y < mHeight; y++)
        {
            for (int x = 0; x < mWidth; x++)
            {
                if (matrix[x, y])
                    canvas.DrawRect(x, y, 1, 1, blackPaint);
            }
        }

        // Optional human-readable text strip.
        if (humanText is not null)
        {
            using var textPaint = new SKPaint
            {
                Color       = SKColors.Black,
                IsAntialias = true,
                TextSize    = 14,
                Typeface    = SKTypeface.FromFamilyName("Courier New", SKFontStyle.Bold),
                TextAlign   = SKTextAlign.Center
            };
            canvas.DrawText(humanText, canvasW / 2f, mHeight + 16, textPaint);
        }

        using var image  = SKImage.FromBitmap(bitmap);
        using var data   = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
