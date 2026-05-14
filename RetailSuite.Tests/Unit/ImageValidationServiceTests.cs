using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetailSuite.Infrastructure.Modules.Images.Services;

namespace RetailSuite.Tests.Unit;

/// <summary>
/// Verifies the image validator detects format from magic bytes (not just the file extension)
/// and enforces the size limit + allow-list.
/// </summary>
public class ImageValidationServiceTests
{
    private static ImageValidationService NewService(long maxBytes = 5 * 1024 * 1024, string[]? allowed = null) =>
        new(Options.Create(new ImageStorageOptions
        {
            MaxUploadBytes    = maxBytes,
            AllowedExtensions = allowed ?? new[] { "png", "jpg", "jpeg", "webp" }
        }),
        NullLogger<ImageValidationService>.Instance);

    private static MemoryStream Stream(params byte[] bytes) => new(bytes);

    [Fact]
    public async Task PngMagicBytes_DetectedAsPng()
    {
        var pngHeader = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D
        };
        var s = Stream(pngHeader);
        var result = await NewService().ValidateAsync(s, s.Length);
        Assert.True(result.IsValid);
        Assert.Equal("png", result.DetectedExtension);
        Assert.Equal("image/png", result.DetectedMimeType);
    }

    [Fact]
    public async Task JpegMagicBytes_DetectedAsJpg()
    {
        var jpgHeader = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        var s = Stream(jpgHeader);
        var result = await NewService().ValidateAsync(s, s.Length);
        Assert.True(result.IsValid);
        Assert.Equal("jpg", result.DetectedExtension);
    }

    [Fact]
    public async Task WebpMagicBytes_DetectedAsWebp()
    {
        // "RIFF" + 4 bytes filesize + "WEBP"
        var webpHeader = new byte[]
        {
            0x52, 0x49, 0x46, 0x46,   // RIFF
            0x00, 0x00, 0x00, 0x00,   // size placeholder
            0x57, 0x45, 0x42, 0x50    // WEBP
        };
        var s = Stream(webpHeader);
        var result = await NewService().ValidateAsync(s, s.Length);
        Assert.True(result.IsValid);
        Assert.Equal("webp", result.DetectedExtension);
    }

    [Fact]
    public async Task ExecutableRenamedAsJpg_Rejected()
    {
        // "MZ" PE header — not an image.
        var fakeJpg = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00 };
        var s = Stream(fakeJpg);
        var result = await NewService().ValidateAsync(s, s.Length);
        Assert.False(result.IsValid);
        Assert.Contains("Unsupported", result.Reason);
    }

    [Fact]
    public async Task OversizeFile_Rejected()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF };
        var s = Stream(bytes);
        var result = await NewService(maxBytes: 2).ValidateAsync(s, 1000);
        Assert.False(result.IsValid);
        Assert.Contains("limit", result.Reason);
    }

    [Fact]
    public async Task EmptyFile_Rejected()
    {
        var s = Stream();
        var result = await NewService().ValidateAsync(s, 0);
        Assert.False(result.IsValid);
        Assert.Contains("empty", result.Reason);
    }

    [Fact]
    public async Task GifDetectedButRejectedWhenNotInAllowList()
    {
        var gif = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 };  // GIF89a
        var s = Stream(gif);
        var result = await NewService().ValidateAsync(s, s.Length);
        Assert.False(result.IsValid);
        Assert.Equal("gif", result.DetectedExtension);
        Assert.Contains("not allowed", result.Reason);
    }
}
