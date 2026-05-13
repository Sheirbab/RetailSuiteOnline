using Microsoft.Extensions.Options;

namespace RetailSuite.Infrastructure.Modules.Images.Services;

public record ImageValidationResult(bool IsValid, string? Reason, string? DetectedMimeType, string? DetectedExtension);

/// <summary>
/// Validates uploaded image bytes by inspecting magic numbers (not just the file extension).
/// Rejects renamed executables, oversize files, and unsupported formats.
/// </summary>
public interface IImageValidationService
{
    /// <summary>
    /// Inspect the start of <paramref name="content"/> to detect the real image format and
    /// validate against allowed types + size cap. Rewinds the stream to 0 before returning.
    /// </summary>
    Task<ImageValidationResult> ValidateAsync(Stream content, long contentLength);
}

public class ImageValidationService : IImageValidationService
{
    private readonly ImageStorageOptions _options;

    public ImageValidationService(IOptions<ImageStorageOptions> options)
    {
        _options = options.Value;
    }

    public async Task<ImageValidationResult> ValidateAsync(Stream content, long contentLength)
    {
        if (content == null)
            return new ImageValidationResult(false, "No content.", null, null);

        if (contentLength <= 0)
            return new ImageValidationResult(false, "Empty file.", null, null);

        if (contentLength > _options.MaxUploadBytes)
            return new ImageValidationResult(
                false,
                $"File too large. Max {_options.MaxUploadBytes / (1024 * 1024)} MB.",
                null, null);

        // Read the first 16 bytes for signature detection.
        var header = new byte[16];
        var read = await content.ReadAsync(header.AsMemory(0, 16));
        if (content.CanSeek) content.Position = 0;

        if (read < 4)
            return new ImageValidationResult(false, "File too small to be a valid image.", null, null);

        var (mime, ext) = DetectFormat(header, read);
        if (mime is null || ext is null)
            return new ImageValidationResult(false, "Unsupported image format (PNG/JPG/WEBP only).", null, null);

        var allowed = _options.AllowedExtensions
            .Select(e => e.Trim().TrimStart('.').ToLowerInvariant())
            .ToHashSet();

        // jpeg/jpg interchangeable
        var canonicalExt = ext == "jpeg" ? "jpg" : ext;
        if (!allowed.Contains(canonicalExt) && !allowed.Contains(ext))
            return new ImageValidationResult(false, $"Format {ext} not allowed.", mime, ext);

        return new ImageValidationResult(true, null, mime, canonicalExt);
    }

    /// <summary>
    /// Inspect magic bytes. Supports PNG, JPEG, WEBP.
    /// References: https://en.wikipedia.org/wiki/List_of_file_signatures
    /// </summary>
    private static (string? mime, string? ext) DetectFormat(byte[] header, int length)
    {
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (length >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return ("image/png", "png");
        }

        // JPEG: FF D8 FF
        if (length >= 3
            && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ("image/jpeg", "jpg");
        }

        // WEBP: 'RIFF' .... 'WEBP'
        if (length >= 12
            && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
            && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return ("image/webp", "webp");
        }

        return (null, null);
    }
}
