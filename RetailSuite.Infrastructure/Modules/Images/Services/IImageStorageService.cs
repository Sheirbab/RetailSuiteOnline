namespace RetailSuite.Infrastructure.Modules.Images.Services;

/// <summary>
/// Where product images live on disk / in blob storage.
/// LocalImageStorageService writes under wwwroot/uploads; an Azure Blob impl can replace it later
/// without touching the controller. The interface deliberately deals in web-relative paths
/// (e.g. "/uploads/.../foo.jpg") so consumers don't have to care about absolute disk paths.
/// </summary>
public interface IImageStorageService
{
    /// <summary>
    /// Persist the stream. Returns the web-relative URL to store on the entity.
    /// Caller is responsible for closing/disposing the input stream.
    /// </summary>
    Task<string> SaveAsync(Guid tenantId, Guid productId, Stream content, string extension);

    /// <summary>Best-effort delete. Missing files are not an error.</summary>
    Task DeleteAsync(string relativePath);
}

/// <summary>Options for image storage. Bind from appsettings.json "Images" section.</summary>
public class ImageStorageOptions
{
    public const string Section = "Images";

    /// <summary>Max upload size in bytes. Default 5 MB.</summary>
    public long MaxUploadBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Allowed extensions (without the dot), lower case. Default png, jpg, jpeg, webp.</summary>
    public string[] AllowedExtensions { get; set; } = new[] { "png", "jpg", "jpeg", "webp" };
}
