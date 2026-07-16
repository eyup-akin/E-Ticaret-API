using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;

namespace ETicaretAPI.Controllers
{
    // Ürüne bağlı yorumlar:  /api/products/5/reviews
    [Route("api/products/{productId}/reviews")]
    [ApiController]
    public class ReviewsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ReviewsController(AppDbContext context)
        {
            _context = context;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // 🟢 GET /api/products/5/reviews — herkese açık, ürünün yorumları
        [HttpGet]
        public async Task<IActionResult> GetReviews(int productId)
        {
            var reviews = await _context.Reviews
                .Where(r => r.ProductId == productId)
                .OrderByDescending(r => r.CreatedAt)
                .Join(_context.Users,
                      r => r.UserId,
                      u => u.Id,
                      (r, u) => new ReviewDto
                      {
                          Id = r.Id,
                          UserName = u.FullName,
                          Rating = r.Rating,
                          Comment = r.Comment,
                          CreatedAt = r.CreatedAt
                      })
                .ToListAsync();

            return Ok(reviews);
        }

        // 🟡 GET /api/products/5/reviews/durum — giriş yapan kullanıcı yorum yapabilir mi?
        // Mobil, "Yorum Yap" butonunu göstersin mi diye bunu soracak.
        [Authorize]
        [HttpGet("durum")]
        public async Task<IActionResult> GetReviewStatus(int productId)
        {
            var userId = GetUserId();

            var zatenYorumladi = await _context.Reviews
                .AnyAsync(r => r.ProductId == productId && r.UserId == userId);

            var teslimAlindi = await TeslimAlindiMi(userId, productId);

            return Ok(new
            {
                yorumYapabilir = teslimAlindi && !zatenYorumladi,
                zatenYorumladi,
                teslimAlindi
            });
        }

        // 🟡 POST /api/products/5/reviews — yorum ekle
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> AddReview(int productId, [FromBody] ReviewCreateDto dto)
        {
            var userId = GetUserId();

            // 1) Ürün var mı?
            var urunVarMi = await _context.Products.AnyAsync(p => p.Id == productId);
            if (!urunVarMi)
            {
                return NotFound(new { mesaj = "Ürün bulunamadı biladerim!" });
            }

            // 2) Puan 1-5 arası mı?
            if (dto.Rating < 1 || dto.Rating > 5)
            {
                return BadRequest(new { mesaj = "Puan 1 ile 5 arasında olmalı!" });
            }

            // 3) Yorum boş mu?
            if (string.IsNullOrWhiteSpace(dto.Comment))
            {
                return BadRequest(new { mesaj = "Yorum boş olamaz!" });
            }

            // 4) UYGUNLUK — bu ürünü içeren, TESLİM EDİLMİŞ siparişi var mı?
            var teslimAlindi = await TeslimAlindiMi(userId, productId);
            if (!teslimAlindi)
            {
                return BadRequest(new { mesaj = "Sadece teslim aldığın ürünlere yorum yapabilirsin." });
            }

            // 5) Daha önce yorum yapmış mı? (tek yorum kuralı — DB'de de unique index var)
            var zatenVar = await _context.Reviews
                .AnyAsync(r => r.ProductId == productId && r.UserId == userId);
            if (zatenVar)
            {
                return BadRequest(new { mesaj = "Bu ürüne zaten yorum yaptın." });
            }

            var review = new Review
            {
                ProductId = productId,
                UserId = userId,
                Rating = dto.Rating,
                Comment = dto.Comment.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Yorumun eklendi biladerim!" });
        }

        // ---- YARDIMCI: kullanıcı bu ürünü teslim aldı mı? ----
        // Kendi siparişleri içinde 'teslim_edildi' olan + bu ürünü içeren biri var mı?
        private async Task<bool> TeslimAlindiMi(int userId, int productId)
        {
            return await _context.Orders
                .Where(o => o.UserId == userId && o.Status == "teslim_edildi")
                .Join(_context.OrderItems,
                      o => o.Id,
                      oi => oi.OrderId,
                      (o, oi) => oi)
                .AnyAsync(oi => oi.ProductId == productId);
        }
    }
}