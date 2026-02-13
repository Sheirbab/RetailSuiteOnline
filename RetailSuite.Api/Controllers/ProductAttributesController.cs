using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Modules.Catalog;
using RetailSuite.Modules.Catalog.Entities;

namespace RetailSuite.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/attributes")]
public class ProductAttributesController : ControllerBase
{
    private readonly RetailDbContext _db;

    public ProductAttributesController(RetailDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<IActionResult> CreateAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Attribute name required.");

        var attribute = new ProductAttribute(name);

        _db.ProductAttributes.Add(attribute);
        await _db.SaveChangesAsync();

        return Ok(attribute.Id);
    }
    [HttpPost("{attributeId}/values")]
    public async Task<IActionResult> CreateValue(Guid attributeId, string value)
    {
        var attributeExists = await _db.ProductAttributes
            .AnyAsync(a => a.Id == attributeId);

        if (!attributeExists)
            return NotFound("Attribute not found.");

        var attrValue = new ProductAttributeValue(attributeId, value);

        _db.ProductAttributeValues.Add(attrValue);
        await _db.SaveChangesAsync();

        return Ok(attrValue.Id);
    }
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var attributes = await _db.ProductAttributes
            .Include(a => _db.ProductAttributeValues
                .Where(v => v.AttributeId == a.Id))
            .ToListAsync();

        return Ok(attributes);
    }
}