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
    [Authorize]
    public class FavoritesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public FavoritesController(AppDbContext context)
        {
            _context = context;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // 🟡 GET /api/favorites — favorilerim
        [HttpGet]
        public async Task<IActionResult> GetFavorites()
        {
            var userId = GetUserId();

            var favorites = await _context.Favorites
                .Where(f => f.UserId == userId)
                .Join(_context.Products,
                      f => f.ProductId,
                      p => p.Id,
                      (f, p) => new FavoriteDto
                      {
                          Id = f.Id,
                          ProductId = p.Id,
                          ProductName = p.Name,
                          ProductPrice = p.Price,
                          Stock = p.Stock,
                          ProductImageUrl = _context.ProductImages
                              .Where(pi => pi.ProductId == p.Id)
                              .OrderByDescending(pi => pi.IsMain)   // önce ana resim
                              .ThenBy(pi => pi.SortOrder)
                              .Select(pi => pi.Url)
                              .FirstOrDefault()
                      })
                .ToListAsync();

            return Ok(favorites);
        }

        // 🟡 POST /api/favorites/5 — favoriye ekle
        [HttpPost("{productId}")]
        public async Task<IActionResult> AddFavorite(int productId)
        {
            var userId = GetUserId();

            var urunVarMi = await _context.Products.AnyAsync(p => p.Id == productId);
            if (!urunVarMi)
            {
                return NotFound(new { mesaj = "Böyle bir ürün yok biladerim!" });
            }

            // Zaten favoride mi?
            var zatenVar = await _context.Favorites
                .AnyAsync(f => f.UserId == userId && f.ProductId == productId);

            if (zatenVar)
            {
                return BadRequest(new { mesaj = "Bu ürün zaten favorilerinde!" });
            }

            _context.Favorites.Add(new Favorite
            {
                UserId = userId,
                ProductId = productId
            });

            await _context.SaveChangesAsync();
            return Ok(new { mesaj = "Favorilere eklendi biladerim!" });
        }

        // 🟡 DELETE /api/favorites/5 — favoriden çıkar
        [HttpDelete("{productId}")]
        public async Task<IActionResult> RemoveFavorite(int productId)
        {
            var userId = GetUserId();

            var favorite = await _context.Favorites
                .FirstOrDefaultAsync(f => f.UserId == userId && f.ProductId == productId);

            if (favorite == null)
            {
                return NotFound(new { mesaj = "Bu ürün favorilerinde yok!" });
            }

            _context.Favorites.Remove(favorite);
            await _context.SaveChangesAsync();
            return Ok(new { mesaj = "Favorilerden çıkarıldı!" });
        }
    }
}