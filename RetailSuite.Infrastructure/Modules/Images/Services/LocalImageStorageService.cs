using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RetailSuite.Infrastructure.Modules.Images.Services;

/// <summary>
/// Stores images under <c>wwwroot/uploads/{tenantId}/products/{productId}-{guid}.{ext}</c>.
/// The matching web URL is <c>/uploads/{tenantId}/products/{productId}-{guid}.{ext}</c>
/// which is already served by <c>app.UseStaticFiles()</c>.
/// </summary>
/// <remarks>
/// Uses <see cref="IHostEnvironment"/> (not <c>IWebHostEnvironment</c>) so the Infrastructure
/// project doesn't take an AspNetCore framework reference. <c>wwwroot</c> is resolved relative
/// to ContentRootPath.
/// </remarks>
public class LocalImageStorageService : IImageStorageService
{
    private readonly IHostEnvironment _env;
    private readonly ILogger<LocalImageStorageService> _logger;

    public LocalImageStorageService(IHostEnvironment env, ILogger<LocalImageStorageService> logger)
    {
        _env    = env;
        _logger = logger;
    }

    public async Task<string> SaveAsync(Guid tenantId, Guid productId, Stream content, string extension)
    {
        var safeExt = NormaliseExtension(extension);
        var fileName = $"{productId:N}-{Guid.NewGuid():N}.{safeExt}";

        var webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");

        var relativeFolder = Path.Combine("uploads", tenantId.ToString("N"), "products");
        var absoluteFolder = Path.Combine(webRoot, relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        var absolutePath = Path.Combine(absoluteFolder, fileName);
        await using (var fs = File.Create(absolutePath))
        {
            await content.CopyToAsync(fs);
        }

        // Always use forward slashes in the URL.
        var webRelative = "/" + Path.Combine(relativeFolder, fileName).Replace('\\', '/');

        _logger.LogInformation(
            "Saved product image: Tenant={TenantId}, Product={ProductId}, Path={Path}",
            tenantId, productId, webRelative);

        return webRelative;
    }

    public Task DeleteAsync(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return Task.CompletedTask;

        // Strip leading slash, resolve against web root.
        var trimmed = relativePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        var webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
        var absolutePath = Path.Combine(webRoot, trimmed);

        // Defence in depth — never delete outside web root.
        var normalisedWebRoot = Path.GetFullPath(webRoot);
        var normalisedTarget  = Path.GetFullPath(absolutePath);
        if (!normalisedTarget.StartsWith(normalisedWebRoot, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Refusing to delete image outside web root: {Path}", absolutePath);
            return Task.CompletedTask;
        }

        try
        {
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
                _logger.LogInformation("Deleted product image: {Path}", absolutePath);
            }
        }
        catch (Exception ex)
        {
            // Don't let a stale-file delete failure break the response.
            _logger.LogWarning(ex, "Failed to delete image file: {Path}", absolutePath);
        }

        return Task.CompletedTask;
    }

    private static string NormaliseExtension(string ext)
    {
        var clean = (ext ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        // Map jpeg-equivalents.
        return clean switch
        {
            "jpeg" => "jpg",
            _      => clean
        };
    }
}
