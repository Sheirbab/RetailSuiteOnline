using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Api.Authorization;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Catalog.Dtos;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Modules.Catalog.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Tree-aware category management. Categories are nested via ParentCategoryId.
/// The storefront uses the tree to drive nested filtering (category + descendants).
/// </summary>
[ApiController]
[Route("api/categories")]
[RequirePermission(Permissions.Categories)]
public class CategoriesController : ControllerBase
{
    private readonly RetailDbContext _db;

    public CategoriesController(RetailDbContext db) => _db = db;

    // -----------------------------------------------------------------
    // GET /api/categories — flat list with parent ids + product counts
    // -----------------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? active)
    {
        var q = _db.Categories.AsQueryable();
        if (active.HasValue) q = q.Where(c => c.IsActive == active.Value);

        var counts = await _db.ProductCategories
            .GroupBy(pc => pc.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count);

        var rows = await q
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(rows.Select(c => new
        {
            c.Id, c.Name, c.Slug, c.ParentCategoryId, c.SortOrder, c.IsActive,
            ProductCount = counts.TryGetValue(c.Id, out var n) ? n : 0
        })));
    }

    // -----------------------------------------------------------------
    // GET /api/categories/tree — nested structure ready for tree UI
    // -----------------------------------------------------------------
    [HttpGet("tree")]
    public async Task<IActionResult> Tree()
    {
        var counts = await _db.ProductCategories
            .GroupBy(pc => pc.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count);

        var all = await _db.Categories
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync();

        // Build nested DTOs in memory — small tree, doesn't justify a CTE.
        CategoryNode ToNode(Category c) => new()
        {
            Id           = c.Id,
            Name         = c.Name,
            Slug         = c.Slug,
            SortOrder    = c.SortOrder,
            IsActive     = c.IsActive,
            ProductCount = counts.TryGetValue(c.Id, out var n) ? n : 0,
            Children     = new()
        };

        var lookup = all.ToDictionary(c => c.Id, ToNode);
        var roots  = new List<CategoryNode>();
        foreach (var c in all)
        {
            var node = lookup[c.Id];
            if (c.ParentCategoryId.HasValue && lookup.TryGetValue(c.ParentCategoryId.Value, out var parent))
                parent.Children.Add(node);
            else
                roots.Add(node);
        }

        return Ok(ApiResponse<object>.Ok(roots));
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse<object>.Fail("Name is required."));

        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? Product.Slugify(request.Name)
            : Product.Slugify(request.Slug);
        slug = await EnsureUniqueSlugAsync(slug, excludeId: null);

        if (request.ParentCategoryId.HasValue
            && !await _db.Categories.AnyAsync(c => c.Id == request.ParentCategoryId.Value))
            return BadRequest(ApiResponse<object>.Fail("Parent category not found."));

        var c = new Category(request.Name, slug, request.ParentCategoryId);
        _db.Categories.Add(c);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new { c.Id, c.Name, c.Slug }));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCategoryRequest request)
    {
        var c = await _db.Categories.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(ApiResponse<object>.Fail("Category not found."));

        if (!string.IsNullOrWhiteSpace(request.Name))
            c.Rename(request.Name);

        if (!string.IsNullOrWhiteSpace(request.Slug)
            && !string.Equals(request.Slug, c.Slug, StringComparison.OrdinalIgnoreCase))
        {
            var slug = await EnsureUniqueSlugAsync(Product.Slugify(request.Slug), excludeId: c.Id);
            c.SetSlug(slug);
        }

        if (request.SortOrder.HasValue)
            c.SetSortOrder(request.SortOrder.Value);

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value) c.Activate(); else c.Deactivate();
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { c.Id, c.Name, c.Slug, c.ParentCategoryId, c.SortOrder, c.IsActive }));
    }

    // -----------------------------------------------------------------
    // POST /api/categories/{id}/move — change parent (with cycle guard)
    // -----------------------------------------------------------------
    [HttpPost("{id:guid}/move")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Move(Guid id, [FromBody] MoveCategoryRequest request)
    {
        var c = await _db.Categories.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(ApiResponse<object>.Fail("Category not found."));

        if (request.NewParentId.HasValue)
        {
            if (request.NewParentId.Value == id)
                return BadRequest(ApiResponse<object>.Fail("Category cannot be its own parent."));

            var parent = await _db.Categories
                .FirstOrDefaultAsync(x => x.Id == request.NewParentId.Value);
            if (parent == null)
                return BadRequest(ApiResponse<object>.Fail("Target parent not found."));

            // Reject move into one of this node's descendants — would create a cycle.
            if (await IsDescendantAsync(request.NewParentId.Value, ancestorId: id))
                return BadRequest(ApiResponse<object>.Fail("Cannot move a category into its own descendant."));
        }

        c.SetParent(request.NewParentId);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { c.Id, c.ParentCategoryId }));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var c = await _db.Categories.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(ApiResponse<object>.Fail("Category not found."));

        // Refuse to delete a category that still has children or products attached —
        // forces the admin to reparent / detach first. Safer than implicit cascades.
        var hasChildren = await _db.Categories.AnyAsync(x => x.ParentCategoryId == id);
        var hasProducts = await _db.ProductCategories.AnyAsync(pc => pc.CategoryId == id);
        if (hasChildren || hasProducts)
        {
            // Soft hide instead.
            c.Deactivate();
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(new
            {
                Deactivated = id,
                Reason      = hasChildren ? "has-children" : "has-products"
            }));
        }

        _db.Categories.Remove(c);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { Deleted = id }));
    }

    // ----- helpers ----------------------------------------------------

    private async Task<bool> IsDescendantAsync(Guid candidateId, Guid ancestorId)
    {
        // Walk up from candidate to root — if we hit ancestor, it's a descendant.
        var current = await _db.Categories
            .Where(c => c.Id == candidateId)
            .Select(c => c.ParentCategoryId)
            .FirstOrDefaultAsync();

        while (current.HasValue)
        {
            if (current.Value == ancestorId) return true;
            current = await _db.Categories
                .Where(c => c.Id == current.Value)
                .Select(c => c.ParentCategoryId)
                .FirstOrDefaultAsync();
        }
        return false;
    }

    private async Task<string> EnsureUniqueSlugAsync(string candidate, Guid? excludeId)
    {
        var slug = candidate;
        var n = 1;
        while (await _db.Categories.AnyAsync(c => c.Slug == slug && (excludeId == null || c.Id != excludeId)))
        {
            n++;
            slug = $"{candidate}-{n}";
        }
        return slug;
    }

    private class CategoryNode
    {
        public Guid    Id           { get; set; }
        public string  Name         { get; set; } = "";
        public string  Slug         { get; set; } = "";
        public int     SortOrder    { get; set; }
        public bool    IsActive     { get; set; }
        public int     ProductCount { get; set; }
        public List<CategoryNode> Children { get; set; } = new();
    }
}

public class UpdateCategoryRequest
{
    public string?  Name      { get; set; }
    public string?  Slug      { get; set; }
    public int?     SortOrder { get; set; }
    public bool?    IsActive  { get; set; }
}

public class MoveCategoryRequest
{
    public Guid? NewParentId { get; set; }
}
