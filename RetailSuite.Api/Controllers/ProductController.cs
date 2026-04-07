using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Modules.Catalog.Dtos;
using RetailSuite.Modules.Catalog.Entities;

//[Authorize(Policy = "AdminOnly")]
[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly RetailDbContext _db;

    public ProductsController(RetailDbContext db)
    {
        _db = db;
    }

    // CREATE PRODUCT
    [HttpPost]
    public async Task<IActionResult> Create(CreateProductRequest request)
    {
        var product = new Product(
            request.Name,
            request.Description);

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        return Ok(product.Id);
    }

    // UPDATE PRODUCT
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, UpdateProductRequest request)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
            return NotFound();

        product.Update(request.Name, request.Description);

        await _db.SaveChangesAsync();

        return Ok();
    }

    // GET PRODUCT WITH VARIANTS
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
            return NotFound();

        return Ok(product);
    }
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var product = await _db.Products
            .Include(p => p.Variants).ToListAsync();

        if (product == null)
            return NotFound();

        return Ok(product);
    }
    // ADD VARIANT
    [HttpPost("{productId}/variants")]
    public async Task<IActionResult> AddVariant(
        Guid productId,
        CreateVariantRequest request)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product == null)
            return NotFound();

        var variant = new ProductVariant(
            productId,
            request.SKU,
            request.Price);

        product.AddVariant(variant);

        await _db.SaveChangesAsync();

        return Ok(variant.Id);
    }
    [Authorize]
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

        return Ok(new
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = items
        });
    }
    [HttpPost("{productId}/categories/{categoryId}")]
    public async Task<IActionResult> AssignCategory(
    Guid productId,
    Guid categoryId)
    {
        var exists = await _db.ProductCategories
            .AnyAsync(pc =>
                pc.ProductId == productId &&
                pc.CategoryId == categoryId);

        if (exists)
            return Ok();

        _db.ProductCategories.Add(
            new ProductCategory(productId, categoryId));

        await _db.SaveChangesAsync();

        return Ok();
    }
}