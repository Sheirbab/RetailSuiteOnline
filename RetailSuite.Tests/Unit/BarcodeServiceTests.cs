using RetailSuite.Infrastructure.Modules.Barcodes.Services;

namespace RetailSuite.Tests.Unit;

/// <summary>
/// Smoke tests for barcode generation. We don't assert pixel content (rendering varies
/// across SkiaSharp versions), only that the output is a non-empty valid PNG header.
/// </summary>
public class BarcodeServiceTests
{
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    [Fact]
    public void GenerateCode128Png_ProducesPngWithMagicBytes()
    {
        var svc = new BarcodeService();
        var png = svc.GenerateCode128Png("SKU-ABC-12345");
        AssertPngMagic(png);
    }

    [Fact]
    public void GenerateCode128Png_ChangesWithDifferentValues()
    {
        var svc = new BarcodeService();
        var a   = svc.GenerateCode128Png("SKU-A");
        var b   = svc.GenerateCode128Png("SKU-B");
        Assert.NotEqual(a.Length == b.Length && a.Take(64).SequenceEqual(b.Take(64)), true);
    }

    [Fact]
    public void GenerateQrPng_ProducesPng()
    {
        var svc = new BarcodeService();
        var png = svc.GenerateQrPng("https://retailsuite.example/v/abc");
        AssertPngMagic(png);
    }

    [Fact]
    public void GenerateCode128Png_RejectsEmptyValue()
    {
        var svc = new BarcodeService();
        Assert.Throws<ArgumentException>(() => svc.GenerateCode128Png(""));
    }

    private static void AssertPngMagic(byte[] png)
    {
        Assert.NotNull(png);
        Assert.True(png.Length > PngMagic.Length, "PNG should be longer than just the magic bytes.");
        for (int i = 0; i < PngMagic.Length; i++)
            Assert.Equal(PngMagic[i], png[i]);
    }
}
