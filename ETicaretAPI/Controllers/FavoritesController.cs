using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;
using ETicaretAPI.Services;   // ⭐ YENİ — MagazaAyarlari

namespace ETicaretAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class FavoritesController : ControllerBase
    {
        private readonly AppDbContext _context;

        // ⭐ YENİ — stok eşiği için (5.3).
        // Eşik panel, rapor ve mobilde AYNI sayı olmalı.
        private readonly MagazaAyarlari _ayarlar;

        public FavoritesController(AppDbContext context, MagazaAyarlari ayarlar)
        {
            _context = context;
            _ayarlar = ayarlar;
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
                // ⭐ YENİ — join'in sağ tarafını daraltıyoruz: pasif ürün
                // favori listesinde çıkmaz.
                //
                // Neden Favorites SATIRI silinmiyor, sadece gizleniyor:
                // Ürün yarın tekrar satışa açılırsa favori kendiliğinden
                // geri gelir. Kaydı silmek geri dönüşü olmayan bir işlem
                // olurdu ve müşteri hiçbir şey yapmadığı halde favorisini
                // kaybederdi.
                //
                // Neden join'in İÇİNDE filtreliyoruz da sonrasında değil:
                // FavoriteDto'ya dönüştükten sonra IsActive bilgisi yok;
                // filtrelemek için DTO'ya gereksiz bir alan eklemek
                // gerekirdi. Kaynakta filtrelemek hem daha az veri taşır
                // hem tek SQL cümlesinde biter.
                .Join(_context.Products.Where(p => p.IsActive),
                      f => f.ProductId,
                      p => p.Id,
                      (f, p) => new FavoriteDto
                      {
                          Id = f.Id,
                          ProductId = p.Id,
                          ProductName = p.Name,
                          ProductPrice = p.Price,
                          // ⭐ DEĞİŞTİ — ham stok yerine türetilmiş durum.
                          //
                          // ⚠️ Hesap SQL'de yapılıyor (bellekte değil):
                          // eşik karşılaştırması basit bir sayı
                          // kıyaslaması ve veritabanı bunu zaten
                          // yapabiliyor. Bellekte yapsaydık ham stoğu
                          // önce DTO'ya taşımak, sonra silmek
                          // gerekirdi — sızıntı riskini kodun içinde
                          // bir adım daha yaşatmak demekti.
                          StokDurumu =
                              p.Stock <= 0 ? "yok" :
                              p.Stock < _ayarlar.StokAzEsigi ? "az" : "var",

                          // Yalnızca "az" durumunda dolu.
                          KalanAdet =
                              p.Stock > 0 && p.Stock < _ayarlar.StokAzEsigi
                                  ? (int?)p.Stock
                                  : null,

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