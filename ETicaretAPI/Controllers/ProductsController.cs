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
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ProductsController(AppDbContext context)
        {
            _context = context;
        }

        // 🟢 GET /api/products
        // Liste + kategoriye göre filtre + arama — hepsi tek endpoint
        [HttpGet]
        public async Task<IActionResult> GetProducts(
            [FromQuery] int? categoryId,
            [FromQuery] string? search)
        {
            // Sorguyu adım adım kuruyoruz
            var query = _context.Products.AsQueryable();

            // categoryId geldiyse filtrele
            if (categoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == categoryId.Value);
            }

            // search geldiyse isimde ara
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(p => p.Name.Contains(search));
            }

            // DTO'ya çevirip listele
            var products = await query
                .Select(p => new ProductDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Price = p.Price,
                    Stock = p.Stock,
                    CategoryId = p.CategoryId
                })
                .ToListAsync();

            return Ok(products);
        }

        // 🟢 GET /api/products/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(int id)
        {
            var product = await _context.Products.FindAsync(id);

            if (product == null)
            {
                return NotFound(new { mesaj = "Ürün bulunamadı biladerim!" });
            }

            var dto = new ProductDto
            {
                Id = product.Id,
                Name = product.Name,
                Price = product.Price,
                Stock = product.Stock,
                CategoryId = product.CategoryId
            };

            return Ok(dto);
        }

        // 🔴 POST /api/products — sadece admin
        [Authorize(Roles = "admin")]
        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] ProductCreateDto dto)
        {

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var product = new Product
            {
                Name = dto.Name,
                Price = dto.Price,
                Stock = dto.Stock,
                CategoryId = dto.CategoryId
            };

            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Ürün eklendi biladerim!", id = product.Id });
        }

        // 🔴 PUT /api/products/5 — sadece admin
        [Authorize(Roles = "admin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(int id, [FromBody] ProductCreateDto dto)
        {

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }


            var product = await _context.Products.FindAsync(id);

            if (product == null)
            {
                return NotFound(new { mesaj = "Güncellenecek ürün bulunamadı!" });
            }

            product.Name = dto.Name;
            product.Price = dto.Price;
            product.Stock = dto.Stock;
            product.CategoryId = dto.CategoryId;

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Ürün güncellendi biladerim!" });
        }

        // 🔴 DELETE /api/products/5 — sadece admin
        [Authorize(Roles = "admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            var product = await _context.Products.FindAsync(id);

            if (product == null)
            {
                return NotFound(new { mesaj = "Silinecek ürün zaten yok!" });
            }

            _context.Products.Remove(product);
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Ürün silindi biladerim!" });
        }
    }
}