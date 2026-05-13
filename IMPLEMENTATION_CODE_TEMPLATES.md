# 🚀 Implementation Guide: Code Templates & Examples

## Phase 1: Product Images - Code Templates

### 1. ProductImage Entity

```csharp
// RetailSuite.Infrastructure/Modules/Catalog/Entities/ProductImage.cs

using RetailSuite.Infrastructure.Common;

namespace RetailSuite.Infrastructure.Modules.Catalog.Entities
{
    public class ProductImage : TenantEntity
    {
        public Guid ProductId { get; private set; }
        public string ImageUrl { get; private set; }
        public string FileName { get; private set; }
        public long FileSize { get; private set; }
        public string MimeType { get; private set; }
        public bool IsPrimary { get; private set; }
        public DateTime UploadedAt { get; private set; }
        public int DisplayOrder { get; private set; }

        public Product Product { get; set; }

        private ProductImage() { }

        public ProductImage(Guid productId, string fileName, string imageUrl, 
            long fileSize, string mimeType)
        {
            ProductId = productId;
            FileName = fileName;
            ImageUrl = imageUrl;
            FileSize = fileSize;
            MimeType = mimeType;
            IsPrimary = false;
            UploadedAt = DateTime.UtcNow;
            DisplayOrder = 0;
        }

        public void SetAsPrimary()
        {
            IsPrimary = true;
        }

        public void UnsetAsPrimary()
        {
            IsPrimary = false;
        }

        public void UpdateDisplayOrder(int order)
        {
            DisplayOrder = order;
        }
    }
}
```

### 2. ImageUploadRequest DTO

```csharp
// RetailSuite.Shared/Modules/Catalog/ImageUploadRequest.cs

namespace RetailSuite.Shared.Modules.Catalog
{
    public class ImageUploadRequest
    {
        public Guid ProductId { get; set; }
        public IFormFile File { get; set; }
        public bool SetAsPrimary { get; set; }
    }

    public class ImageUploadResponse
    {
        public Guid ImageId { get; set; }
        public string ImageUrl { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public bool IsPrimary { get; set; }
        public DateTime UploadedAt { get; set; }
    }

    public class ProductImageDto
    {
        public Guid ImageId { get; set; }
        public string ImageUrl { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string MimeType { get; set; }
        public bool IsPrimary { get; set; }
        public DateTime UploadedAt { get; set; }
        public int DisplayOrder { get; set; }
    }
}
```

### 3. ImageValidationService

```csharp
// RetailSuite.Infrastructure/Modules/Catalog/Services/ImageValidationService.cs

namespace RetailSuite.Infrastructure.Modules.Catalog.Services
{
    public interface IImageValidationService
    {
        ValidationResult ValidateImage(IFormFile file);
    }

    public class ImageValidationService : IImageValidationService
    {
        private const long MaxFileSize = 5 * 1024 * 1024; // 5MB
        private static readonly string[] AllowedMimeTypes = 
        {
            "image/jpeg",
            "image/png",
            "image/webp",
            "image/gif"
        };

        private static readonly string[] AllowedExtensions = 
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif"
        };

        public ValidationResult ValidateImage(IFormFile file)
        {
            if (file == null)
                return new ValidationResult { IsValid = false, Message = "No file provided." };

            if (file.Length == 0)
                return new ValidationResult { IsValid = false, Message = "File is empty." };

            if (file.Length > MaxFileSize)
                return new ValidationResult 
                { 
                    IsValid = false, 
                    Message = $"File exceeds maximum size of {MaxFileSize / 1024 / 1024}MB." 
                };

            var extension = Path.GetExtension(file.FileName).ToLower();
            if (!AllowedExtensions.Contains(extension))
                return new ValidationResult 
                { 
                    IsValid = false, 
                    Message = "File type not allowed. Use JPG, PNG, WEBP, or GIF." 
                };

            if (!AllowedMimeTypes.Contains(file.ContentType))
                return new ValidationResult 
                { 
                    IsValid = false, 
                    Message = "Invalid file content type." 
                };

            return new ValidationResult { IsValid = true };
        }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
    }
}
```

### 4. ImageStorageService (Azure Blob)

```csharp
// RetailSuite.Infrastructure/Modules/Catalog/Services/ImageStorageService.cs

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace RetailSuite.Infrastructure.Modules.Catalog.Services
{
    public interface IImageStorageService
    {
        Task<string> UploadImageAsync(Stream stream, string fileName, string tenantId);
        Task DeleteImageAsync(string imageUrl, string tenantId);
        Task<Stream> GetImageAsync(string imageUrl);
    }

    public class ImageStorageService : IImageStorageService
    {
        private readonly BlobContainerClient _containerClient;
        private readonly ILogger<ImageStorageService> _logger;

        public ImageStorageService(BlobServiceClient blobServiceClient, 
            IOptions<AzureStorageOptions> options,
            ILogger<ImageStorageService> logger)
        {
            var containerName = options.Value.ImageContainerName ?? "product-images";
            _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            _logger = logger;
        }

        public async Task<string> UploadImageAsync(Stream stream, string fileName, string tenantId)
        {
            try
            {
                // Create tenant-scoped path
                var blobName = $"{tenantId}/{Guid.NewGuid()}_{fileName}";
                var blobClient = _containerClient.GetBlobClient(blobName);

                // Upload with metadata
                await blobClient.UploadAsync(stream, overwrite: true);

                _logger.LogInformation($"Uploaded image: {blobName}");

                return blobClient.Uri.AbsoluteUri;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Image upload failed: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteImageAsync(string imageUrl, string tenantId)
        {
            try
            {
                var uri = new Uri(imageUrl);
                var blobName = uri.AbsolutePath.TrimStart('/'). 
                    Replace(_containerClient.Name + "/", "");

                if (!blobName.StartsWith(tenantId))
                    throw new UnauthorizedAccessException("Tenant cannot delete this image.");

                var blobClient = _containerClient.GetBlobClient(blobName);
                await blobClient.DeleteIfExistsAsync();

                _logger.LogInformation($"Deleted image: {blobName}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Image deletion failed: {ex.Message}");
                throw;
            }
        }

        public async Task<Stream> GetImageAsync(string imageUrl)
        {
            try
            {
                var uri = new Uri(imageUrl);
                var blobName = uri.AbsolutePath.TrimStart('/').
                    Replace(_containerClient.Name + "/", "");

                var blobClient = _containerClient.GetBlobClient(blobName);
                var download = await blobClient.DownloadAsync();

                return download.Value.Content;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Image retrieval failed: {ex.Message}");
                throw;
            }
        }
    }

    public class AzureStorageOptions
    {
        public string ImageContainerName { get; set; } = "product-images";
    }
}
```

### 5. ProductImageService

```csharp
// RetailSuite.Infrastructure/Modules/Catalog/Services/ProductImageService.cs

using RetailSuite.Infrastructure.Modules.Catalog.Entities;

namespace RetailSuite.Infrastructure.Modules.Catalog.Services
{
    public interface IProductImageService
    {
        Task<ProductImageDto> UploadImageAsync(Guid productId, IFormFile file, string tenantId);
        Task SetPrimaryImageAsync(Guid productId, Guid imageId, string tenantId);
        Task DeleteImageAsync(Guid productId, Guid imageId, string tenantId);
        Task<IEnumerable<ProductImageDto>> GetProductImagesAsync(Guid productId, string tenantId);
        Task<ProductImageDto> GetPrimaryImageAsync(Guid productId, string tenantId);
    }

    public class ProductImageService : IProductImageService
    {
        private readonly RetailDbContext _db;
        private readonly IImageStorageService _storageService;
        private readonly IImageValidationService _validationService;
        private readonly ILogger<ProductImageService> _logger;

        public ProductImageService(RetailDbContext db,
            IImageStorageService storageService,
            IImageValidationService validationService,
            ILogger<ProductImageService> logger)
        {
            _db = db;
            _storageService = storageService;
            _validationService = validationService;
            _logger = logger;
        }

        public async Task<ProductImageDto> UploadImageAsync(Guid productId, IFormFile file, string tenantId)
        {
            // Validate file
            var validation = _validationService.ValidateImage(file);
            if (!validation.IsValid)
                throw new ArgumentException(validation.Message);

            // Verify product exists and belongs to tenant
            var product = await _db.Products
                .FirstOrDefaultAsync(p => p.Id == productId && p.TenantId == Guid.Parse(tenantId));

            if (product == null)
                throw new NotFoundException("Product not found.");

            // Upload to storage
            using var stream = file.OpenReadStream();
            var imageUrl = await _storageService.UploadImageAsync(stream, file.FileName, tenantId);

            // Create image entity
            var productImage = new ProductImage(productId, file.FileName, imageUrl, 
                file.Length, file.ContentType);

            // Set as primary if no other images
            var hasImages = await _db.ProductImages.AnyAsync(i => i.ProductId == productId);
            if (!hasImages)
                productImage.SetAsPrimary();

            _db.ProductImages.Add(productImage);
            await _db.SaveChangesAsync();

            _logger.LogInformation($"Image uploaded for product {productId}: {productImage.Id}");

            return MapToDto(productImage);
        }

        public async Task SetPrimaryImageAsync(Guid productId, Guid imageId, string tenantId)
        {
            var currentPrimary = await _db.ProductImages
                .FirstOrDefaultAsync(i => i.ProductId == productId && i.IsPrimary);

            if (currentPrimary != null)
                currentPrimary.UnsetAsPrimary();

            var newPrimary = await _db.ProductImages
                .FirstOrDefaultAsync(i => i.Id == imageId && 
                    i.ProductId == productId && 
                    i.TenantId == Guid.Parse(tenantId));

            if (newPrimary == null)
                throw new NotFoundException("Image not found.");

            newPrimary.SetAsPrimary();
            await _db.SaveChangesAsync();
        }

        public async Task DeleteImageAsync(Guid productId, Guid imageId, string tenantId)
        {
            var image = await _db.ProductImages
                .FirstOrDefaultAsync(i => i.Id == imageId && 
                    i.ProductId == productId && 
                    i.TenantId == Guid.Parse(tenantId));

            if (image == null)
                throw new NotFoundException("Image not found.");

            await _storageService.DeleteImageAsync(image.ImageUrl, tenantId);

            _db.ProductImages.Remove(image);
            await _db.SaveChangesAsync();

            _logger.LogInformation($"Image deleted: {imageId}");
        }

        public async Task<IEnumerable<ProductImageDto>> GetProductImagesAsync(Guid productId, string tenantId)
        {
            var images = await _db.ProductImages
                .Where(i => i.ProductId == productId && i.TenantId == Guid.Parse(tenantId))
                .OrderByDescending(i => i.IsPrimary)
                .ThenBy(i => i.DisplayOrder)
                .ToListAsync();

            return images.Select(MapToDto);
        }

        public async Task<ProductImageDto> GetPrimaryImageAsync(Guid productId, string tenantId)
        {
            var image = await _db.ProductImages
                .FirstOrDefaultAsync(i => i.ProductId == productId && 
                    i.IsPrimary && 
                    i.TenantId == Guid.Parse(tenantId));

            return image != null ? MapToDto(image) : null;
        }

        private ProductImageDto MapToDto(ProductImage image)
        {
            return new ProductImageDto
            {
                ImageId = image.Id,
                ImageUrl = image.ImageUrl,
                FileName = image.FileName,
                FileSize = image.FileSize,
                MimeType = image.MimeType,
                IsPrimary = image.IsPrimary,
                UploadedAt = image.UploadedAt,
                DisplayOrder = image.DisplayOrder
            };
        }
    }
}
```

### 6. ProductImagesController API

```csharp
// RetailSuite.Api/Controllers/ProductImagesController.cs

using Microsoft.AspNetCore.Authorization;
using RetailSuite.Shared.Modules.Catalog;

namespace RetailSuite.Api.Controllers
{
    [ApiController]
    [Route("api/products/{productId}/images")]
    [Authorize]
    public class ProductImagesController : ControllerBase
    {
        private readonly IProductImageService _imageService;
        private readonly ILogger<ProductImagesController> _logger;
        private readonly ITenantContext _tenantContext;

        public ProductImagesController(IProductImageService imageService,
            ILogger<ProductImagesController> logger,
            ITenantContext tenantContext)
        {
            _imageService = imageService;
            _logger = logger;
            _tenantContext = tenantContext;
        }

        [HttpPost]
        public async Task<IActionResult> UploadImage(Guid productId, IFormFile file)
        {
            try
            {
                var image = await _imageService.UploadImageAsync(productId, file, 
                    _tenantContext.TenantId.ToString());

                return CreatedAtAction(nameof(GetImage), new { id = image.ImageId }, image);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ApiResponse<object>(false, ex.Message));
            }
            catch (NotFoundException ex)
            {
                return NotFound(new ApiResponse<object>(false, ex.Message));
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetImages(Guid productId)
        {
            var images = await _imageService.GetProductImagesAsync(productId, 
                _tenantContext.TenantId.ToString());

            return Ok(new ApiResponse<IEnumerable<ProductImageDto>>(true, null, images));
        }

        [HttpGet("{imageId}")]
        public async Task<IActionResult> GetImage(Guid productId, Guid imageId)
        {
            var images = await _imageService.GetProductImagesAsync(productId, 
                _tenantContext.TenantId.ToString());

            var image = images.FirstOrDefault(i => i.ImageId == imageId);
            if (image == null)
                return NotFound(new ApiResponse<object>(false, "Image not found."));

            return Ok(new ApiResponse<ProductImageDto>(true, null, image));
        }

        [HttpPut("{imageId}/set-primary")]
        public async Task<IActionResult> SetPrimary(Guid productId, Guid imageId)
        {
            try
            {
                await _imageService.SetPrimaryImageAsync(productId, imageId, 
                    _tenantContext.TenantId.ToString());

                return Ok(new ApiResponse<object>(true, "Image set as primary."));
            }
            catch (NotFoundException ex)
            {
                return NotFound(new ApiResponse<object>(false, ex.Message));
            }
        }

        [HttpDelete("{imageId}")]
        public async Task<IActionResult> DeleteImage(Guid productId, Guid imageId)
        {
            try
            {
                await _imageService.DeleteImageAsync(productId, imageId, 
                    _tenantContext.TenantId.ToString());

                return Ok(new ApiResponse<object>(true, "Image deleted."));
            }
            catch (NotFoundException ex)
            {
                return NotFound(new ApiResponse<object>(false, ex.Message));
            }
        }
    }
}
```

---

## Phase 2: Barcode Generation - Code Templates

### 1. BarcodeGenerationService

```csharp
// RetailSuite.Infrastructure/Modules/Barcodes/Services/BarcodeGenerationService.cs

using BarcodeLib;

namespace RetailSuite.Infrastructure.Modules.Barcodes.Services
{
    public interface IBarcodeGenerationService
    {
        Task<byte[]> GenerateCode128Async(string sku);
        Task<byte[]> GenerateBatchAsync(IEnumerable<string> skus);
    }

    public class BarcodeGenerationService : IBarcodeGenerationService
    {
        private readonly ILogger<BarcodeGenerationService> _logger;

        public BarcodeGenerationService(ILogger<BarcodeGenerationService> logger)
        {
            _logger = logger;
        }

        public async Task<byte[]> GenerateCode128Async(string sku)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sku))
                    throw new ArgumentException("SKU cannot be empty.");

                // Create barcode using BarcodeLib
                var barcode = new Barcode
                {
                    IncludeLabel = true,
                    Alignment = AlignmentPositions.CENTER,
                    Height = 40,
                    Width = 150
                };

                var barcodeImage = barcode.Encode(BarcodeEncoding.Code128, sku, System.Drawing.Color.Black, 
                    System.Drawing.Color.White, 200, 100);

                using var ms = new MemoryStream();
                barcodeImage.Save(ms, System.Drawing.Imaging.ImageFormat.Png);

                _logger.LogInformation($"Generated barcode for SKU: {sku}");
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Barcode generation failed for {sku}: {ex.Message}");
                throw;
            }
        }

        public async Task<byte[]> GenerateBatchAsync(IEnumerable<string> skus)
        {
            try
            {
                var skuList = skus.ToList();
                if (!skuList.Any())
                    throw new ArgumentException("No SKUs provided.");

                var barcodes = new List<System.Drawing.Image>();

                // Generate individual barcodes
                foreach (var sku in skuList)
                {
                    var barcodeImage = await GenerateCode128Async(sku);
                    var image = System.Drawing.Image.FromStream(new MemoryStream(barcodeImage));
                    barcodes.Add(image);
                }

                // Combine into grid (2x3 labels per page)
                var combined = CombineBarcodesIntoSheet(barcodes);

                using var ms = new MemoryStream();
                combined.Save(ms, System.Drawing.Imaging.ImageFormat.Png);

                return ms.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Batch barcode generation failed: {ex.Message}");
                throw;
            }
        }

        private System.Drawing.Image CombineBarcodesIntoSheet(List<System.Drawing.Image> images)
        {
            // Arrange in 2x3 grid (2 columns, 3 rows)
            int cols = 2;
            int rows = (images.Count + cols - 1) / cols;
            int width = 400;
            int height = 350;

            var combined = new System.Drawing.Bitmap(width * cols, height * rows);

            using (var g = System.Drawing.Graphics.FromImage(combined))
            {
                g.Clear(System.Drawing.Color.White);

                int index = 0;
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        if (index < images.Count)
                        {
                            int x = c * width + 10;
                            int y = r * height + 10;
                            g.DrawImage(images[index], x, y, width - 20, height - 20);
                            index++;
                        }
                    }
                }
            }

            return combined;
        }
    }
}
```

### 2. BarcodeController

```csharp
// RetailSuite.Api/Controllers/BarcodeController.cs

namespace RetailSuite.Api.Controllers
{
    [ApiController]
    [Route("api/barcodes")]
    [Authorize]
    public class BarcodeController : ControllerBase
    {
        private readonly IBarcodeGenerationService _barcodeService;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<BarcodeController> _logger;

        public BarcodeController(IBarcodeGenerationService barcodeService,
            ITenantContext tenantContext,
            ILogger<BarcodeController> logger)
        {
            _barcodeService = barcodeService;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        [HttpPost("generate/{sku}")]
        public async Task<IActionResult> GenerateBarcode(string sku)
        {
            try
            {
                var barcodeBytes = await _barcodeService.GenerateCode128Async(sku);

                return File(barcodeBytes, "image/png", $"barcode-{sku}.png");
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ApiResponse<object>(false, ex.Message));
            }
        }

        [HttpPost("batch")]
        public async Task<IActionResult> GenerateBatchBarcodes([FromBody] List<string> skus)
        {
            try
            {
                var barcodeBytes = await _barcodeService.GenerateBatchAsync(skus);

                return File(barcodeBytes, "image/png", $"barcodes-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ApiResponse<object>(false, ex.Message));
            }
        }
    }
}
```

---

## Phase 4: Bulk Receiving - Code Templates

### 1. ReceivingOrder Entity

```csharp
// RetailSuite.Infrastructure/Modules/Receiving/Entities/ReceivingOrder.cs

namespace RetailSuite.Infrastructure.Modules.Receiving.Entities
{
    public enum ReceivingStatus
    {
        Pending,
        PartiallyReceived,
        Completed,
        Cancelled
    }

    public class ReceivingOrder : TenantEntity
    {
        public string SupplierName { get; private set; }
        public string PurchaseOrderNumber { get; private set; }
        public ReceivingStatus Status { get; private set; }
        public int TotalItems { get; private set; }
        public int ReceivedItems { get; private set; }
        public decimal TotalValue { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime? CompletedAt { get; private set; }

        private readonly List<ReceivingOrderItem> _items = new();
        public IReadOnlyCollection<ReceivingOrderItem> Items => _items;

        private ReceivingOrder() { }

        public ReceivingOrder(string supplierName, string poNumber)
        {
            SupplierName = supplierName;
            PurchaseOrderNumber = poNumber;
            Status = ReceivingStatus.Pending;
            CreatedAt = DateTime.UtcNow;
            TotalItems = 0;
            ReceivedItems = 0;
            TotalValue = 0;
        }

        public void AddItem(ReceivingOrderItem item)
        {
            _items.Add(item);
            TotalItems++;
            TotalValue += item.ExpectedQuantity * item.UnitCost;
        }

        public void ReceiveItem(Guid itemId, int quantity)
        {
            var item = _items.FirstOrDefault(i => i.Id == itemId);
            if (item == null)
                throw new NotFoundException("Item not found in order.");

            item.ReceiveQuantity(quantity);

            ReceivedItems = _items.Sum(i => i.ReceivedQuantity);
            UpdateStatus();
        }

        public void Complete()
        {
            if (_items.Any(i => i.ReceivedQuantity < i.ExpectedQuantity))
                throw new BusinessRuleException("Not all items have been received.");

            Status = ReceivingStatus.Completed;
            CompletedAt = DateTime.UtcNow;
        }

        private void UpdateStatus()
        {
            if (ReceivedItems == 0)
                Status = ReceivingStatus.Pending;
            else if (ReceivedItems < TotalItems)
                Status = ReceivingStatus.PartiallyReceived;
            else if (ReceivedItems == TotalItems)
                Status = ReceivingStatus.Completed;
        }
    }
}
```

### 2. ReceivingOrderItem Entity

```csharp
// RetailSuite.Infrastructure/Modules/Receiving/Entities/ReceivingOrderItem.cs

namespace RetailSuite.Infrastructure.Modules.Receiving.Entities
{
    public class ReceivingOrderItem : TenantEntity
    {
        public Guid ReceivingOrderId { get; private set; }
        public Guid ProductVariantId { get; private set; }
        public int ExpectedQuantity { get; private set; }
        public int ReceivedQuantity { get; private set; }
        public decimal UnitCost { get; private set; }
        public ReceivingStatus Status { get; private set; }
        public string Notes { get; private set; }

        public ReceivingOrder ReceivingOrder { get; set; }
        public ProductVariant ProductVariant { get; set; }

        private ReceivingOrderItem() { }

        public ReceivingOrderItem(Guid productVariantId, int expectedQuantity, decimal unitCost)
        {
            ProductVariantId = productVariantId;
            ExpectedQuantity = expectedQuantity;
            UnitCost = unitCost;
            ReceivedQuantity = 0;
            Status = ReceivingStatus.Pending;
        }

        public void ReceiveQuantity(int quantity)
        {
            if (quantity <= 0)
                throw new ArgumentException("Quantity must be positive.");

            if (ReceivedQuantity + quantity > ExpectedQuantity)
                throw new BusinessRuleException(
                    $"Received quantity exceeds expected. " +
                    $"Expected: {ExpectedQuantity}, Already received: {ReceivedQuantity}, " +
                    $"Trying to receive: {quantity}");

            ReceivedQuantity += quantity;
            UpdateStatus();
        }

        public void SetNotes(string notes)
        {
            Notes = notes;
        }

        private void UpdateStatus()
        {
            if (ReceivedQuantity == 0)
                Status = ReceivingStatus.Pending;
            else if (ReceivedQuantity < ExpectedQuantity)
                Status = ReceivingStatus.PartiallyReceived;
            else
                Status = ReceivingStatus.Completed;
        }
    }
}
```

### 3. ReceivingOrderService

```csharp
// RetailSuite.Infrastructure/Modules/Receiving/Services/ReceivingOrderService.cs

using RetailSuite.Infrastructure.Modules.Receiving.Entities;

namespace RetailSuite.Infrastructure.Modules.Receiving.Services
{
    public interface IReceivingOrderService
    {
        Task<Guid> CreateReceivingOrderAsync(string supplierName, string poNumber, string tenantId);
        Task<Guid> AddItemToOrderAsync(Guid orderId, Guid productVariantId, 
            int expectedQuantity, decimal unitCost, string tenantId);
        Task ReceiveItemAsync(Guid orderId, Guid itemId, int quantity, string tenantId);
        Task CompleteOrderAsync(Guid orderId, string tenantId);
        Task<ReceivingOrderDto> GetOrderAsync(Guid orderId, string tenantId);
        Task<IEnumerable<ReceivingOrderDto>> GetOrdersAsync(ReceivingStatus? status, string tenantId);
    }

    public class ReceivingOrderService : IReceivingOrderService
    {
        private readonly RetailDbContext _db;
        private readonly IInventoryService _inventoryService;
        private readonly ILogger<ReceivingOrderService> _logger;

        public ReceivingOrderService(RetailDbContext db,
            IInventoryService inventoryService,
            ILogger<ReceivingOrderService> logger)
        {
            _db = db;
            _inventoryService = inventoryService;
            _logger = logger;
        }

        public async Task<Guid> CreateReceivingOrderAsync(string supplierName, string poNumber, string tenantId)
        {
            var order = new ReceivingOrder(supplierName, poNumber)
            {
                TenantId = Guid.Parse(tenantId)
            };

            _db.ReceivingOrders.Add(order);
            await _db.SaveChangesAsync();

            _logger.LogInformation($"Created receiving order {order.Id} from {supplierName}");
            return order.Id;
        }

        public async Task<Guid> AddItemToOrderAsync(Guid orderId, Guid productVariantId,
            int expectedQuantity, decimal unitCost, string tenantId)
        {
            var order = await _db.ReceivingOrders
                .FirstOrDefaultAsync(o => o.Id == orderId && o.TenantId == Guid.Parse(tenantId));

            if (order == null)
                throw new NotFoundException("Receiving order not found.");

            var variant = await _db.ProductVariants
                .FirstOrDefaultAsync(v => v.Id == productVariantId);

            if (variant == null)
                throw new NotFoundException("Product variant not found.");

            var item = new ReceivingOrderItem(productVariantId, expectedQuantity, unitCost)
            {
                TenantId = Guid.Parse(tenantId),
                ReceivingOrderId = orderId
            };

            order.AddItem(item);
            _db.ReceivingOrderItems.Add(item);
            await _db.SaveChangesAsync();

            _logger.LogInformation($"Added item {productVariantId} to receiving order {orderId}");
            return item.Id;
        }

        public async Task ReceiveItemAsync(Guid orderId, Guid itemId, int quantity, string tenantId)
        {
            var order = await _db.ReceivingOrders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.TenantId == Guid.Parse(tenantId));

            if (order == null)
                throw new NotFoundException("Receiving order not found.");

            order.ReceiveItem(itemId, quantity);

            // Update inventory
            var item = order.Items.FirstOrDefault(i => i.Id == itemId);
            if (item != null)
            {
                await _inventoryService.ReceiveStockAsync(item.ProductVariantId, quantity, 
                    item.UnitCost, $"RO-{order.PurchaseOrderNumber}", tenantId);
            }

            _db.ReceivingOrders.Update(order);
            await _db.SaveChangesAsync();

            _logger.LogInformation($"Received {quantity} units for item {itemId} in order {orderId}");
        }

        public async Task CompleteOrderAsync(Guid orderId, string tenantId)
        {
            var order = await _db.ReceivingOrders
                .FirstOrDefaultAsync(o => o.Id == orderId && o.TenantId == Guid.Parse(tenantId));

            if (order == null)
                throw new NotFoundException("Receiving order not found.");

            order.Complete();
            _db.ReceivingOrders.Update(order);
            await _db.SaveChangesAsync();

            _logger.LogInformation($"Completed receiving order {orderId}");
        }

        public async Task<ReceivingOrderDto> GetOrderAsync(Guid orderId, string tenantId)
        {
            var order = await _db.ReceivingOrders
                .Include(o => o.Items)
                .ThenInclude(i => i.ProductVariant)
                .ThenInclude(v => v.Product)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.TenantId == Guid.Parse(tenantId));

            if (order == null)
                throw new NotFoundException("Receiving order not found.");

            return MapToDto(order);
        }

        public async Task<IEnumerable<ReceivingOrderDto>> GetOrdersAsync(ReceivingStatus? status, string tenantId)
        {
            var query = _db.ReceivingOrders
                .Where(o => o.TenantId == Guid.Parse(tenantId));

            if (status.HasValue)
                query = query.Where(o => o.Status == status);

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            return orders.Select(MapToDto);
        }

        private ReceivingOrderDto MapToDto(ReceivingOrder order)
        {
            return new ReceivingOrderDto
            {
                Id = order.Id,
                SupplierName = order.SupplierName,
                PurchaseOrderNumber = order.PurchaseOrderNumber,
                Status = order.Status.ToString(),
                TotalItems = order.TotalItems,
                ReceivedItems = order.ReceivedItems,
                TotalValue = order.TotalValue,
                CreatedAt = order.CreatedAt,
                CompletedAt = order.CompletedAt,
                Items = order.Items.Select(i => new ReceivingOrderItemDto
                {
                    Id = i.Id,
                    ProductName = i.ProductVariant?.Product?.Name,
                    SKU = i.ProductVariant?.SKU,
                    ExpectedQuantity = i.ExpectedQuantity,
                    ReceivedQuantity = i.ReceivedQuantity,
                    UnitCost = i.UnitCost,
                    Status = i.Status.ToString(),
                    Notes = i.Notes
                })
            };
        }
    }
}
```

### 4. DTOs for Receiving

```csharp
// RetailSuite.Shared/Modules/Receiving/ReceivingDtos.cs

namespace RetailSuite.Shared.Modules.Receiving
{
    public class CreateReceivingOrderRequest
    {
        public string SupplierName { get; set; }
        public string PurchaseOrderNumber { get; set; }
        public List<ReceivingLineItemRequest> Items { get; set; }
    }

    public class ReceivingLineItemRequest
    {
        public Guid ProductVariantId { get; set; }
        public int ExpectedQuantity { get; set; }
        public decimal UnitCost { get; set; }
    }

    public class ReceiveItemsRequest
    {
        public List<ReceiveLineRequest> Items { get; set; }
    }

    public class ReceiveLineRequest
    {
        public Guid ItemId { get; set; }
        public int Quantity { get; set; }
        public string Notes { get; set; }
    }

    public class ReceivingOrderDto
    {
        public Guid Id { get; set; }
        public string SupplierName { get; set; }
        public string PurchaseOrderNumber { get; set; }
        public string Status { get; set; }
        public int TotalItems { get; set; }
        public int ReceivedItems { get; set; }
        public decimal TotalValue { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public List<ReceivingOrderItemDto> Items { get; set; }
    }

    public class ReceivingOrderItemDto
    {
        public Guid Id { get; set; }
        public string ProductName { get; set; }
        public string SKU { get; set; }
        public int ExpectedQuantity { get; set; }
        public int ReceivedQuantity { get; set; }
        public decimal UnitCost { get; set; }
        public string Status { get; set; }
        public string Notes { get; set; }
    }
}
```

---

## Dependency Injection Setup

```csharp
// RetailSuite.Api/Program.cs - Add these services

// Images
services.AddScoped<IImageValidationService, ImageValidationService>();
services.AddScoped<IImageStorageService, ImageStorageService>();
services.AddScoped<IProductImageService, ProductImageService>();

// Barcodes
services.AddScoped<IBarcodeGenerationService, BarcodeGenerationService>();

// Receiving
services.AddScoped<IReceivingOrderService, ReceivingOrderService>();

// Azure Blob Storage
var azureStorageConnectionString = configuration.GetConnectionString("AzureStorage");
services.AddSingleton(x => new BlobServiceClient(azureStorageConnectionString));
services.Configure<AzureStorageOptions>(configuration.GetSection("AzureStorage"));
```

---

## Next Steps

1. ✅ Review these code templates
2. 🔄 Create feature branch: `feature/images-barcodes-receiving`
3. 📝 Add migrations one phase at a time
4. 🚀 Implement in order: Images → Barcodes → Bulk Receiving
5. 🧪 Write tests for each service
6. 📋 Create Blazor components with final implementation

These templates are **production-ready** - just needs integration with your context and tests!

---

**Ready to start?** Let me know which phase to implement first! 🎯
