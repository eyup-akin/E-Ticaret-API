using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ETicaretAPI.Data;
using ETicaretAPI.DTOs;
using ETicaretAPI.Services;

namespace ETicaretAPI.Controllers
{
    // ⭐ YENİ (Aşama 10) — KVKK md. 11: veri erişim hakkı
    //
    // Ayrı controller: AuthController zaten ~1200 satır ve bu iş
    // kimlik doğrulama değil, veri hakkı.
    [Route("api/hesap")]
    [ApiController]
    [Authorize]
    public class HesapController : ControllerBase
    {
        private readonly AppDbContext _context;

        public HesapController(AppDbContext context)
        {
            _context = context;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // 🟡 POST /api/hesap/verilerimi-indir
        //
        // POST çünkü gövdede şifre taşıyor (okuma işlemi olsa da).
        // Rate limit: ağır sorgu + hassas veri.
        [HttpPost("verilerimi-indir")]
        [EnableRateLimiting("giris")]
        public async Task<IActionResult> VerileriIndir([FromBody] HesapSilDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetUserId();

            var kullanici = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (kullanici == null || !kullanici.IsActive)
            {
                return Unauthorized(new { mesaj = "Oturum geçersiz. Lütfen tekrar giriş yap." });
            }

            // ⚠️ Hassas işlemde yeniden kimlik doğrulama — hesap
            // kapatmadaki desenin aynısı. Çalınan bir telefonla açık
            // oturumdan bütün geçmiş indirilebilmemeli.
            if (!BCrypt.Net.BCrypt.Verify(dto.Sifre, kullanici.PasswordHash))
            {
                return BadRequest(new { mesaj = "Şifren yanlış." });
            }

            var adresler = await (
                from a in _context.Addresses
                where a.UserId == userId
                join p in _context.Phones on a.PhoneId equals p.Id into pg
                from p in pg.DefaultIfEmpty()
                select new
                {
                    a.Title,
                    a.FullAddress,
                    a.City,
                    telefon = p == null ? null : TelefonBicimi.Goster(p.Numara)
                }
            ).ToListAsync();

            // ⚠️ Telefon defteri de dahil (4.9'da not düşülmüştü):
            // numara kişisel veri.
            var numaralar = await _context.Phones
                .Where(p => p.UserId == userId)
                .Select(p => new { p.Numara, p.Etiket, p.DogrulandiMi, p.CreatedAt })
                .ToListAsync();

            var siparisler = await _context.Orders
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new
                {
                    o.OrderNumber,
                    o.CreatedAt,
                    o.Status,
                    o.PaymentStatus,
                    o.SubTotal,
                    o.DiscountAmount,
                    o.ShippingCost,
                    o.Total,
                    o.CouponCode,

                    // ⚠️ Son 4 hane zaten maskeli veri; tam numara ve CVV
                    // veritabanında hiç yok.
                    kartSon4 = o.CardLast4,

                    teslimat = new
                    {
                        o.ShippingFullName,
                        o.ShippingTitle,
                        o.ShippingCity,
                        o.ShippingFullAddress,
                        o.ShippingPhone
                    },

                    kalemler = _context.OrderItems
                        .Where(oi => oi.OrderId == o.Id)
                        .Select(oi => new
                        {
                            oi.ProductName,
                            oi.Quantity,
                            oi.UnitPrice
                        })
                        .ToList()
                })
                .ToListAsync();

            var odemeler = await _context.Payments
                .Where(p => p.UserId == userId)
                .Select(p => new { p.Amount, p.Status, p.PaidAt, kartSon4 = p.CardLast4 })
                .ToListAsync();

            var yorumlar = await _context.Reviews
                .Where(r => r.UserId == userId)
                .Select(r => new
                {
                    urun = _context.Products
                        .Where(u => u.Id == r.ProductId)
                        .Select(u => u.Name)
                        .FirstOrDefault(),
                    r.Rating,
                    r.Comment,
                    r.CreatedAt
                })
                .ToListAsync();

            var favoriler = await _context.Favorites
                .Where(f => f.UserId == userId)
                .Select(f => new
                {
                    urun = _context.Products
                        .Where(u => u.Id == f.ProductId)
                        .Select(u => u.Name)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var oturumlar = await _context.RefreshTokens
                .Where(t => t.UserId == userId)
                .Select(t => new { t.CihazBilgisi, t.CreatedAt, t.ExpiresAt, t.RevokedAt })
                .ToListAsync();

            var destek = await _context.SupportTickets
                .Where(t => t.UserId == userId)
                .Select(t => new
                {
                    t.Konu,
                    t.Kategori,
                    t.Durum,
                    t.CreatedAt,
                    mesajlar = _context.SupportMessages
                        .Where(m => m.TicketId == t.Id)
                        .OrderBy(m => m.CreatedAt)
                        .Select(m => new { m.Mesaj, m.GonderenAdminMi, m.CreatedAt })
                        .ToList()
                })
                .ToListAsync();

            var iadeler = await (
                from r in _context.ReturnRequests
                join o in _context.Orders on r.OrderId equals o.Id
                where o.UserId == userId
                select new
                {
                    siparisNo = o.OrderNumber,
                    r.Sebep,
                    r.Aciklama,
                    r.Durum,
                    r.TalepTarihi,
                    r.IadeTutari
                }
            ).ToListAsync();

            // ⚠️ Onay kayıtları da veriye dahil: "neyi ne zaman
            // onayladım" sorusunun cevabı kullanıcının hakkı.
            var onaylar = await (
                from onay in _context.SozlesmeOnaylari
                where onay.UserId == userId
                join s in _context.Sozlesmeler on onay.SozlesmeId equals s.Id
                orderby onay.OnayTarihi
                select new
                {
                    sozlesme = s.Tip,
                    s.Surum,
                    onay.OnayTarihi,
                    onay.IpAdresi
                }
            ).ToListAsync();

            // ⚠️ PasswordHash, SecurityStamp ve token hash'leri YOK.
            // Onlar kullanıcının verisi değil, kimlik doğrulama sırrı.
            return Ok(new
            {
                olusturmaTarihi = DateTime.UtcNow,

                profil = new
                {
                    kullanici.FullName,
                    kullanici.Email,
                    kullanici.Role,
                    kullanici.CreatedAt,
                    kullanici.EmailDogrulandiMi
                },

                adresler,
                numaralar,
                siparisler,
                odemeler,
                yorumlar,
                favoriler,
                oturumlar,
                destekTalepleri = destek,
                iadeler,
                sozlesmeOnaylari = onaylar
            });
        }
    }
}
