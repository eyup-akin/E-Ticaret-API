using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ETicaretAPI.Data;

namespace ETicaretAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PaymentsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public PaymentsController(AppDbContext context)
        {
            _context = context;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // 🟡 GET /api/payments — kendi ödeme geçmişim
        [HttpGet]
        public async Task<IActionResult> GetMyPayments()
        {
            var userId = GetUserId();

            var payments = await _context.Payments
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.PaidAt)
                .Select(p => new
                {
                    p.Id,
                    p.OrderId,
                    p.Amount,
                    p.CardLast4,
                    p.Status,
                    p.PaidAt
                })
                .ToListAsync();

            return Ok(payments);
        }

        // ==========================================================
        //  ADMIN BÖLÜMÜ
        // ==========================================================

        // 🔴 GET /api/admin/payments
        //     ?search=&status=&startDate=2026-07-01&endDate=2026-07-31&page=1&pageSize=10
        [Authorize(Roles = "admin")]
        [HttpGet("/api/admin/payments")]
        public async Task<IActionResult> GetAllPayments(
            [FromQuery] string? search,
            [FromQuery] string? status,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 10;
            }

            // Ödeme + müşteri birleşimi (tek sorgu)
            var query = from p in _context.Payments
                        join u in _context.Users on p.UserId equals u.Id
                        select new { p, u };

            // --- FİLTRELER ---
            if (!string.IsNullOrWhiteSpace(search))
            {
                var arama = search.Trim();

                query = query.Where(x =>
                    x.u.FullName.Contains(arama) ||
                    x.u.Email.Contains(arama) ||
                    x.p.OrderId.ToString().Contains(arama) ||
                    x.p.CardLast4.Contains(arama));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(x => x.p.Status == status);
            }

            if (startDate.HasValue)
            {
                query = query.Where(x => x.p.PaidAt >= startDate.Value.Date);
            }

            if (endDate.HasValue)
            {
                // DİKKAT: "31 Temmuz'a kadar" derken 31 Temmuz DAHİL olmalı.
                // PaidAt <= 31.07 00:00 dersek, o gün saat 14:00'teki ödeme dışarıda kalır.
                // Çözüm: ertesi günün başlangıcından KÜÇÜK olsun.
                var bitis = endDate.Value.Date.AddDays(1);
                query = query.Where(x => x.p.PaidAt < bitis);
            }

            // --- ÖZET (filtreye uyan TÜM kayıtlar üzerinden, sayfa değil) ---
            var brutGelir = await query
                .Where(x => x.p.Status == "basarili")
                .SumAsync(x => (decimal?)x.p.Amount) ?? 0;

            var iadeToplam = await query
                .Where(x => x.p.Status == "iade")
                .SumAsync(x => (decimal?)x.p.Amount) ?? 0;

            var basariliSayi = await query.CountAsync(x => x.p.Status == "basarili");
            var iadeSayi = await query.CountAsync(x => x.p.Status == "iade");

            var toplam = await query.CountAsync();

            // --- SAYFALAMA ---
            var odemeler = await query
                .OrderByDescending(x => x.p.PaidAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    id = x.p.Id,
                    siparisId = x.p.OrderId,
                    musteriAdi = x.u.FullName,
                    musteriEmail = x.u.Email,
                    tutar = x.p.Amount,
                    kartSon4 = x.p.CardLast4,
                    durum = x.p.Status,
                    tarih = x.p.PaidAt
                })
                .ToListAsync();

            var toplamSayfa = (int)Math.Ceiling(toplam / (double)pageSize);

            return Ok(new
            {
                odemeler = odemeler,
                toplam = toplam,
                sayfa = page,
                sayfaBoyutu = pageSize,
                toplamSayfa = toplamSayfa,

                ozet = new
                {
                    brutGelir = brutGelir,
                    iadeToplam = iadeToplam,
                    netGelir = brutGelir - iadeToplam,   // kasadaki gerçek para
                    basariliSayi = basariliSayi,
                    iadeSayi = iadeSayi,

                    ortalamaSepet = basariliSayi > 0
                        ? Math.Round(brutGelir / basariliSayi, 2)
                        : 0
                }
            });
        }

        // 🔴 GET /api/admin/revenue?months=6 — aylık gelir kırılımı (grafik için)
        [Authorize(Roles = "admin")]
        [HttpGet("/api/admin/revenue")]
        public async Task<IActionResult> GetRevenue([FromQuery] int months = 6)
        {
            if (months < 1 || months > 24)
            {
                months = 6;
            }

            var simdi = DateTime.UtcNow;

            // Kaç ay geriye gideceğiz? (bu ay dahil)
            var baslangic = new DateTime(simdi.Year, simdi.Month, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddMonths(-(months - 1));

            // Ham ödemeleri çek, sonra bellekte aylara böl.
            // Not: SQL'de GROUP BY yıl/ay da yapılabilirdi ama boş ayları
            // (hiç ödeme olmayan aylar) SQL döndürmez — grafikte delik olur.
            // Biz her ayı tek tek yürüyerek boş ayları da 0 ile dolduruyoruz.
            var ham = await _context.Payments
                .Where(p => p.PaidAt >= baslangic)
                .Select(p => new { p.PaidAt, p.Amount, p.Status })
                .ToListAsync();

            var aylik = new List<object>();

            for (int i = 0; i < months; i++)
            {
                var ayBasi = baslangic.AddMonths(i);
                var aySonu = ayBasi.AddMonths(1);

                var oAy = ham.Where(p => p.PaidAt >= ayBasi && p.PaidAt < aySonu).ToList();

                var brut = oAy.Where(p => p.Status == "basarili").Sum(p => p.Amount);
                var iade = oAy.Where(p => p.Status == "iade").Sum(p => p.Amount);

                aylik.Add(new
                {
                    ay = ayBasi.ToString("yyyy-MM"),
                    brut = brut,
                    iade = iade,
                    net = brut - iade
                });
            }

            // Tüm zamanların özeti
            var tumBrut = await _context.Payments
                .Where(p => p.Status == "basarili")
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            var tumIade = await _context.Payments
                .Where(p => p.Status == "iade")
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            return Ok(new
            {
                aylik = aylik,

                tumZamanlar = new
                {
                    brutGelir = tumBrut,
                    iadeToplam = tumIade,
                    netGelir = tumBrut - tumIade
                }
            });
        }
    }
}