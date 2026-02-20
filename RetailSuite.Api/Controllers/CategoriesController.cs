using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Catalog.Dtos;
using RetailSuite.Modules.Catalog.Entities;

namespace RetailSuite.Api.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    [ApiController]
    [Route("api/[controller]")]
    public class CategoriesController : ControllerBase
    {
        private readonly RetailDbContext _db;

        public CategoriesController(RetailDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateCategoryRequest request)
        {
            var category = new Category(
                request.Name,
                request.Slug,
                request.ParentCategoryId);

            _db.Categories.Add(category);
            await _db.SaveChangesAsync();

            return Ok(category.Id);
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var categories = await _db.Categories.ToListAsync();
            return Ok(categories);
        }
    }
}
