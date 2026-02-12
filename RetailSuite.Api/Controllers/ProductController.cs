using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Modules.Catalog;
using RetailSuite.Modules.Catalog.Dtos;
using RetailSuite.Modules.Catalog.Entities;

namespace RetailSuite.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly CatalogDbContext _db;

    public ProductsController(CatalogDbContext db)
    {
        _db = db;
    }

    // ------------------------------------
    // CREATE PRODUCT
    // ------------------------------------
    [HttpPost]
    public async Task<IActionResult> Create(CreateProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Product name is required.");

        var duplicate = request.Variants.GroupBy(v => string.Join("-", v.AttributeValueIds.OrderBy(x => x))).Any(g => g.Count() > 1);

        if (duplicate)
            return BadRequest("Duplicate variant combinations detected.");

        var product = new Product(request.Name, request.Description);

        foreach (var variantReq in request.Variants)
        {
            var validIds = await _db.ProductAttributeValues.Where(v => variantReq.AttributeValueIds.Contains(v.Id))
                                                            .Select(v => v.Id)
                                                            .ToListAsync();

            if (validIds.Count != variantReq.AttributeValueIds.Count)
                return BadRequest("Invalid attribute value detected.");

            var variant = new ProductVariant(
                product.Id,
                variantReq.SKU,
                variantReq.Price);

            foreach (var attrValueId in variantReq.AttributeValueIds)
            {
                variant.AttributeValues.ToList()
                    .Add(new VariantAttributeValue
                    {
                        ProductVariantId = variant.Id,
                        ProductAttributeValueId = attrValueId
                    });
            }

            _db.ProductVariants.Add(variant);
        }

        _db.Products.Add(product);

        await _db.SaveChangesAsync();

        return Ok(product.Id);
    }

    // ------------------------------------
    // GET ALL PRODUCTS
    // ------------------------------------
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var products = await _db.Products
            .Include(p => p.Variants)
                .ThenInclude(v => v.AttributeValues)
                    .ThenInclude(av => av.ProductAttributeValue)
            .ToListAsync();

        var response = products.Select(p => new ProductResponse
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Variants = p.Variants.Select(v => new ProductVariantResponse
            {
                Id = v.Id,
                SKU = v.SKU,
                Price = v.Price,
                Attributes = v.AttributeValues
                    .Select(a => a.ProductAttributeValue.Value)
                    .ToList()
            }).ToList()
        });

        return Ok(response);
    }

    // ------------------------------------
    // GET SINGLE PRODUCT
    // ------------------------------------
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
                .ThenInclude(v => v.AttributeValues)
                    .ThenInclude(av => av.ProductAttributeValue)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
            return NotFound();

        var response = new ProductResponse
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            Variants = product.Variants.Select(v => new ProductVariantResponse
            {
                Id = v.Id,
                SKU = v.SKU,
                Price = v.Price,
                Attributes = v.AttributeValues
                    .Select(a => a.ProductAttributeValue.Value)
                    .ToList()
            }).ToList()
        };

        return Ok(response);
    }

    // ------------------------------------
    // DEACTIVATE PRODUCT
    // ------------------------------------
    [HttpPut("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var product = await _db.Products.FindAsync(id);

        if (product == null)
            return NotFound();

        typeof(Product)
            .GetProperty("IsActive")?
            .SetValue(product, false);

        await _db.SaveChangesAsync();

        return Ok();
    }
}