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
            // Her kategorinin ürün sayısını da getiriyoruz.
            // DİKKAT: Bunu tek sorguda yapıyoruz.
            // Yanlış yol: önce kategorileri çek, sonra her biri için ayrı COUNT sorgusu at
            // (5 kategori = 6 sorgu → "N+1 problemi"). SQL bunu tek seferde yapabilir.
            var categories = await _context.Categories
                .Select(c => new CategoryDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    ProductCount = _context.Products.Count(p => p.CategoryId == c.Id)
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

            var urunSayisi = await _context.Products.CountAsync(p => p.CategoryId == id);

            return Ok(new CategoryDto
            {
                Id = category.Id,
                Name = category.Name,
                ProductCount = urunSayisi
            });
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

            // ⭐ YENİ — İÇİ DOLU KATEGORİ SİLİNEMEZ
            // Bu kategoriye bağlı ürün varsa, SQL zaten foreign key hatası fırlatırdı.
            // Ama o hata teknik ve anlaşılmaz. Biz önden kontrol edip
            // admin'in anlayacağı bir dille söylüyoruz.
            var urunSayisi = await _context.Products.CountAsync(p => p.CategoryId == id);

            if (urunSayisi > 0)
            {
                return BadRequest(new
                {
                    mesaj = $"Bu kategoride {urunSayisi} ürün var. Önce ürünleri başka bir kategoriye taşı veya sil."
                });
            }

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Kategori silindi biladerim!" });
        }
    }
}