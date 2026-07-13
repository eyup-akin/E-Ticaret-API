using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;

namespace ETicaretAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // tüm sepet işlemleri giriş ister
    public class CartController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CartController(AppDbContext context)
        {
            _context = context;
        }

        // Token'dan giriş yapmış kullanıcının id'sini okur
        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // 🟡 GET /api/cart — benim sepetim
        [HttpGet]
        public async Task<IActionResult> GetCart()
        {
            var userId = GetUserId();

            // Sepet öğelerini ürün bilgisiyle birleştir (join)
            var cart = await _context.CartItems
                .Where(c => c.UserId == userId)
                .Join(_context.Products,
                      c => c.ProductId,
                      p => p.Id,
                      (c, p) => new CartItemDto
                      {
                          Id = c.Id,
                          ProductId = p.Id,
                          ProductName = p.Name,
                          ProductPrice = p.Price,
                          Quantity = c.Quantity
                      })
                .ToListAsync();

            return Ok(cart);
        }

        // 🟡 POST /api/cart — sepete ekle
        [HttpPost]
        public async Task<IActionResult> AddToCart([FromBody] CartAddDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetUserId();

            // Ürün gerçekten var mı?
            var urunVarMi = await _context.Products.AnyAsync(p => p.Id == dto.ProductId);
            if (!urunVarMi)
            {
                return NotFound(new { mesaj = "Böyle bir ürün yok biladerim!" });
            }

            // Bu ürün zaten sepette var mı? Varsa adet artır
            var mevcut = await _context.CartItems
                .FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == dto.ProductId);

            if (mevcut != null)
            {
                mevcut.Quantity += dto.Quantity;
            }
            else
            {
                _context.CartItems.Add(new CartItem
                {
                    UserId = userId,
                    ProductId = dto.ProductId,
                    Quantity = dto.Quantity
                });
            }

            await _context.SaveChangesAsync();
            return Ok(new { mesaj = "Ürün sepete eklendi biladerim!" });
        }

        // 🟡 PUT /api/cart/5 — adet güncelle
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateQuantity(int id, [FromBody] CartAddDto dto)
        {
            var userId = GetUserId();

            // Sadece KENDİ sepet öğesini güncelleyebilir
            var item = await _context.CartItems
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

            if (item == null)
            {
                return NotFound(new { mesaj = "Sepet öğesi bulunamadı!" });
            }

            item.Quantity = dto.Quantity;
            await _context.SaveChangesAsync();
            return Ok(new { mesaj = "Adet güncellendi!" });
        }

        // 🟡 DELETE /api/cart/5 — sepetten çıkar
        [HttpDelete("{id}")]
        public async Task<IActionResult> RemoveFromCart(int id)
        {
            var userId = GetUserId();

            var item = await _context.CartItems
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

            if (item == null)
            {
                return NotFound(new { mesaj = "Sepet öğesi bulunamadı!" });
            }

            _context.CartItems.Remove(item);
            await _context.SaveChangesAsync();
            return Ok(new { mesaj = "Ürün sepetten çıkarıldı!" });
        }
    }
}