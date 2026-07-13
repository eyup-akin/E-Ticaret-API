using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;

namespace ETicaretAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CategoriesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CategoriesController(AppDbContext context)
        {
            _context = context;
        }

        // 🟢 GET /api/categories
        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.Categories
                .Select(c => new CategoryDto
                {
                    Id = c.Id,
                    Name = c.Name
                })
                .ToListAsync();

            return Ok(categories);
        }

        // 🟢 GET /api/categories/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetCategory(int id)
        {
            var category = await _context.Categories.FindAsync(id);

            if (category == null)
            {
                return NotFound(new { mesaj = "Kategori bulunamadı biladerim!" });
            }

            return Ok(new CategoryDto { Id = category.Id, Name = category.Name });
        }

        // 🔴 POST /api/categories — sadece admin
        [Authorize(Roles = "admin")]
        [HttpPost]
        public async Task<IActionResult> CreateCategory([FromBody] CategoryCreateDto dto)
        {
            // validation kontrolü
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var category = new Category { Name = dto.Name };

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Kategori eklendi biladerim!", id = category.Id });
        }

        // 🔴 PUT /api/categories/5 — sadece admin
        [Authorize(Roles = "admin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCategory(int id, [FromBody] CategoryCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var category = await _context.Categories.FindAsync(id);

            if (category == null)
            {
                return NotFound(new { mesaj = "Güncellenecek kategori bulunamadı!" });
            }

            category.Name = dto.Name;
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Kategori güncellendi biladerim!" });
        }

        // 🔴 DELETE /api/categories/5 — sadece admin
        [Authorize(Roles = "admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCategory(int id)
        {
            var category = await _context.Categories.FindAsync(id);

            if (category == null)
            {
                return NotFound(new { mesaj = "Silinecek kategori zaten yok!" });
            }

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Kategori silindi biladerim!" });
        }
    }
}