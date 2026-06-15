using Microsoft.Extensions.Logging;
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
    /// validate against allowed types + size cap. Caller is responsible for rewinding the stream
    /// if it intends to read it again afterwards.
    /// </summary>
    Task<ImageValidationResult> ValidateAsync(Stream content, long contentLength);
}

public class ImageValidationService : IImageValidationService
{
    private readonly ImageStorageOptions _options;
    private readonly ILogger<ImageValidationService> _logger;

    public ImageValidationService(
        IOptions<ImageStorageOptions> options,
        ILogger<ImageValidationService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ImageValidationResult> ValidateAsync(Stream content, long contentLength)
    {
        if (content == null)
            return new(false, "No content.", null, null);

        if (contentLength <= 0)
            return new(false, "File is empty.", null, null);

        if (contentLength > _options.MaxUploadBytes)
            return new(false, $"File exceeds the {_options.MaxUploadBytes / (1024 * 1024)} MB limit.", null, null);

        // Read up to 16 bytes from the start to identify magic bytes.
        var header = new byte[16];
        var read = await content.ReadAsync(header.AsMemory(0, header.Length));
        if (read < 4)
            return new(false, "File too short to identify image type.", null, null);

        var (ext, mime) = SniffFormat(header, read);
        if (ext is null || mime is null)
            return new(false, "Unsupported or unrecognised image format.", null, null);

        // Cross-check the detected extension against the allow-list.
        var allowed = _options.AllowedExtensions ?? Array.Empty<string>();
        if (!allowed.Any(a => string.Equals(a, ext, StringComparison.OrdinalIgnoreCase)
                            || (ext == "jpg" && string.Equals(a, "jpeg", StringComparison.OrdinalIgnoreCase))))
        {
            _logger.LogWarning("Rejected upload: detected {Ext} not in allow-list.", ext);
            return new ImageValidationResult(
                IsValid:           false,
                Reason:            $"Format {ext} is not allowed.",
                DetectedMimeType:  mime,
                DetectedExtension: ext);
        }

        return new ImageValidationResult(
            IsValid:           true,
            Reason:            null,
            DetectedMimeType:  mime,
            DetectedExtension: ext);
    }

    /// <summary>
    /// Identify common image formats by their magic bytes.
    /// Returns (extension, mime) tuple, or (null, null) when format is unknown.
    /// </summary>
    private static (string? ext, string? mime) SniffFormat(byte[] header, int read)
    {
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (read >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return ("png", "image/png");
        }

        // JPEG: FF D8 FF
        if (read >= 3
            && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ("jpg", "image/jpeg");
        }

        // WEBP: "RIFF"....."WEBP"
        if (read >= 12
            && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
            && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return ("webp", "image/webp");
        }

        // GIF87a / GIF89a — recognise even though it's not in default allow-list, so the
        // service can report "format not allowed" rather than "unrecognised".
        if (read >= 6
            && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38
            && (header[4] == 0x37 || header[4] == 0x39) && header[5] == 0x61)
        {
            return ("gif", "image/gif");
        }

        return (null, null);
    }
}
