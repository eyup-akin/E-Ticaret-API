using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;   // ⭐ YENİ — [EnableRateLimiting] için
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ETicaretAPI.Data;
using ETicaretAPI.DTOs;
using ETicaretAPI.Services;

namespace ETicaretAPI.Controllers
{
    [Route("api/coupons")]
    [ApiController]
    [Authorize]
    public class CouponsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly KuponServisi _kuponServisi;

        public CouponsController(AppDbContext context, KuponServisi kuponServisi)
        {
            _context = context;
            _kuponServisi = kuponServisi;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // 🟡 POST /api/coupons/dogrula — sepette kupon dene
        //
        // Kuponu UYGULAMIYOR, sadece "geçerli mi ve ne kadar iner" diyor.
        // Gerçek uygulama sipariş oluşurken yapılır (transaction içinde).
        //
        // Neden ayrı? Müşteri kuponu yazınca anında görmek ister ama
        // henüz sipariş vermemiştir. Bu bir ÖNİZLEME.
        // ⭐ YENİ — brute-force koruması.
        //
        // Özniteliği CONTROLLER'a değil METODA koyduk. Şu an controller'da
        // tek metot var, ikisi de aynı sonucu verirdi. Ama ileride buraya
        // "kuponlarım" gibi listeleme uçları eklenirse onların bu limite
        // takılması yanlış olur — sınır, korunması gereken İŞE ait,
        // dosyaya değil.
        [EnableRateLimiting("kupon")]
        [HttpPost("dogrula")]
        public async Task<IActionResult> Dogrula([FromBody] CouponValidateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetUserId();

            // Sepeti veritabanından çekiyoruz — istekten DEĞİL.
            // Müşteri sahte sepet gönderip indirim şişiremesin.
            var sepet = await SepetiGetirAsync(userId);

            if (sepet.Count == 0)
            {
                return BadRequest(new { mesaj = "Sepetin boş." });
            }

            var sonuc = await _kuponServisi.DogrulaAsync(dto.Code, userId, sepet);

            if (!sonuc.Gecerli)
            {
                return BadRequest(new { mesaj = sonuc.Mesaj });
            }

            var araToplam = sepet.Sum(k => k.BirimFiyat * k.Adet);

            return Ok(new
            {
                mesaj = sonuc.Mesaj,
                kod = sonuc.Kupon!.Code,
                aciklama = sonuc.Kupon.Description,
                araToplam = araToplam,
                indirim = sonuc.IndirimTutari,
                yeniToplam = araToplam - sonuc.IndirimTutari
            });
        }

        // Sepeti kupon hesabı için uygun biçimde çeker.
        // OrdersController da aynı veriye ihtiyaç duyacak.
        private async Task<List<SepetKalemi>> SepetiGetirAsync(int userId)
        {
            return await _context.CartItems
                .Where(ci => ci.UserId == userId)
                .Join(_context.Products,
                      ci => ci.ProductId,
                      p => p.Id,
                      (ci, p) => new SepetKalemi
                      {
                          ProductId = p.Id,
                          CategoryId = p.CategoryId,
                          Adet = ci.Quantity,
                          BirimFiyat = p.Price
                      })
                .ToListAsync();
        }
    }
}