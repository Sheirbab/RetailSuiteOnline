using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Modules.Catalog.Dtos;
using RetailSuite.Modules.Catalog.Entities;
using RetailSuite.Shared;
using RetailSuite.Infrastructure.Modules.Identity;

[Authorize(Policy = "StaffOrAdmin")]
[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ICurrentUserContext _currentUser;

    public ProductsController(RetailDbContext db, IWebHostEnvironment env, ICurrentUserContext currentUser)
    {
        _db = db;
        _env = env;
        _currentUser = currentUser;
    }

    // GET /api/products?page=1&pageSize=20
    [HttpGet]
    public async Task<IActionResult> Get(int page = 1, int pageSize = 20)
    {
        var query = _db.Products.Include(p => p.Variants).AsQueryable();
        var total = await query.CountAsync();
        var items = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new ApiResponse<object>(true, null, new
        {
            Total    = total,
            Page     = page,
            PageSize = pageSize,
            Items    = items
        }));
    }

    // GET /api/products/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
            return NotFound(ApiResponse<object>.Fail("Product not found."));

        return Ok(new ApiResponse<object>(true, null, product));
    }

    // GET /api/products/variants  — flat list for POS
    [HttpGet("variants")]
    public async Task<IActionResult> GetVariants()
    {
        var variants = await _db.ProductVariants
            .Include(v => v.Product)
            .Where(v => v.IsActive)
            .Select(v => new
            {
                v.Id,
                v.SKU,
                v.Barcode,
                v.Price,
                v.CostPrice,
                v.StockQuantity,
                ProductId   = v.ProductId,
                ProductName = v.Product.Name
            })
            .ToListAsync();

        return Ok(new ApiResponse<object>(true, null, variants));
    }

    // GET /api/products/search
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        string? keyword,
        Guid? categoryId,
        int page = 1,
        int pageSize = 20)
    {
        var query = _db.Products
            .Include(p => p.Variants)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(p => p.Name.Contains(keyword));

        if (categoryId.HasValue)
            query = query.Where(p =>
                _db.ProductCategories.Any(pc =>
                    pc.ProductId == p.Id &&
                    pc.CategoryId == categoryId.Value));

        var total = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new ApiResponse<object>(true, null, new
        {
            Total    = total,
            Page     = page,
            PageSize = pageSize,
            Items    = items
        }));
    }

    // POST /api/products
    [HttpPost]
    public async Task<IActionResult> Create(CreateProductRequest request)
    {
        var product = new Product(request.Name, request.Description);
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        return Ok(new ApiResponse<Guid>(true, "Product created.", product.Id));
    }

    // PUT /api/products/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, UpdateProductRequest request)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
            return NotFound(ApiResponse<object>.Fail("Product not found."));

        product.Update(request.Name, request.Description);
        await _db.SaveChangesAsync();

        return Ok(new ApiResponse<object>(true, "Product updated.", null));
    }

    // POST /api/products/{productId}/variants
    [HttpPost("{productId}/variants")]
    public async Task<IActionResult> AddVariant(Guid productId, CreateVariantRequest request)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product == null)
            return NotFound(ApiResponse<object>.Fail("Product not found."));

        var variant = new ProductVariant(productId, request.SKU, request.Price);
        if (request.TaxRate > 0) variant.SetTaxRate(request.TaxRate);
        product.AddVariant(variant);
        await _db.SaveChangesAsync();

        return Ok(new ApiResponse<Guid>(true, "Variant added.", variant.Id));
    }

    // POST /api/products/{id}/image
    [HttpPost("{id}/image")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("No file provided."));

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!allowedExtensions.Contains(ext))
            return BadRequest(ApiResponse<object>.Fail("Only jpg, png, and webp images are allowed."));

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(ApiResponse<object>.Fail("Image must be under 5 MB."));

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product == null)
            return NotFound(ApiResponse<object>.Fail("Product not found."));

        var tenantId = _currentUser.TenantId;
        var uploadDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", tenantId.ToString());
        Directory.CreateDirectory(uploadDir);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var filePath  = Path.Combine(uploadDir, fileName);

        await using (var stream = System.IO.File.Create(filePath))
            await file.CopyToAsync(stream);

        var relativeUrl = $"/uploads/{tenantId}/{fileName}";
        product.SetImageUrl(relativeUrl);
        await _db.SaveChangesAsync();

        return Ok(new ApiResponse<string>(true, "Image uploaded.", relativeUrl));
    }

    // POST /api/products/{productId}/categories/{categoryId}
    [HttpPost("{productId}/categories/{categoryId}")]
    public async Task<IActionResult> AssignCategory(Guid productId, Guid categoryId)
    {
        var exists = await _db.ProductCategories
            .AnyAsync(pc => pc.ProductId == productId && pc.CategoryId == categoryId);

        if (!exists)
        {
            _db.ProductCategories.Add(new ProductCategory(productId, categoryId));
            await _db.SaveChangesAsync();
        }

        return Ok(new ApiResponse<object>(true, "Category assigned.", null));
    }
}
