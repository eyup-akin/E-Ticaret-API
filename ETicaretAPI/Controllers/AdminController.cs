using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;

namespace ETicaretAPI.Controllers
{
    [Route("api/admin")]
    [ApiController]
    [Authorize(Roles = "admin")] // TÜM admin controller'ı sadece admin'e açık
    public class AdminController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AdminController(AppDbContext context)
        {
            _context = context;
        }

        // 🔴 GET /api/admin/users — tüm müşteriler
        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _context.Users
                .Select(u => new
                {
                    u.Id,
                    u.FullName,
                    u.Email,
                    u.Role
                    // PasswordHash ASLA gönderilmiyor — güvenlik
                })
                .ToListAsync();

            return Ok(users);
        }

        // 🔴 GET /api/admin/dashboard — temel özet
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var toplamSiparis = await _context.Orders.CountAsync();
            var toplamUrun = await _context.Products.CountAsync();
            var toplamMusteri = await _context.Users.CountAsync(u => u.Role == "customer");
            var toplamGelir = await _context.Payments
                .Where(p => p.Status == "basarili")
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            return Ok(new
            {
                toplamSiparis = toplamSiparis,
                toplamUrun = toplamUrun,
                toplamMusteri = toplamMusteri,
                toplamGelir = toplamGelir
            });
        }

        // ⭐ 🔴 GET /api/admin/stats — DETAYLI istatistikler
        // Bütün ağır hesap SQL'de yapılır, tarayıcıya sadece SONUÇ gider.
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var simdi = DateTime.UtcNow;
            var buAyBasi = new DateTime(simdi.Year, simdi.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var gecenAyBasi = buAyBasi.AddMonths(-1);

            // ---------- 1) BU AY vs GEÇEN AY ----------
            var buAyGelir = await _context.Payments
                .Where(p => p.Status == "basarili" && p.PaidAt >= buAyBasi)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            var gecenAyGelir = await _context.Payments
                .Where(p => p.Status == "basarili"
                         && p.PaidAt >= gecenAyBasi
                         && p.PaidAt < buAyBasi)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            var buAySiparis = await _context.Orders
                .CountAsync(o => o.CreatedAt >= buAyBasi);

            // Geçen aya göre yüzde değişim
            decimal degisimYuzde = 0;
            if (gecenAyGelir > 0)
            {
                degisimYuzde = (buAyGelir - gecenAyGelir) / gecenAyGelir * 100;
            }

            // ---------- 2) SON 7 GÜNÜN GÜNLÜK GELİRİ (grafik için) ----------
            var yediGunOnce = simdi.Date.AddDays(-6);

            // Önce ham ödemeleri çek, sonra bellekte günlere böl
            var hamOdemeler = await _context.Payments
                .Where(p => p.Status == "basarili" && p.PaidAt >= yediGunOnce)
                .Select(p => new { p.PaidAt, p.Amount })
                .ToListAsync();

            var gunlukGelir = new List<object>();
            for (int i = 0; i < 7; i++)
            {
                var gun = yediGunOnce.AddDays(i);
                var toplam = hamOdemeler
                    .Where(p => p.PaidAt.Date == gun)
                    .Sum(p => p.Amount);

                gunlukGelir.Add(new
                {
                    tarih = gun.ToString("yyyy-MM-dd"),
                    gelir = toplam
                });
            }

            // ---------- 3) EN ÇOK SATAN 5 ÜRÜN ----------
            var satisOzeti = await _context.OrderItems
                .GroupBy(oi => oi.ProductId)
                .Select(g => new
                {
                    productId = g.Key,
                    adet = g.Sum(x => x.Quantity),
                    ciro = g.Sum(x => x.Quantity * x.UnitPrice)
                })
                .OrderByDescending(x => x.adet)
                .Take(5)
                .ToListAsync();

            var idler = satisOzeti.Select(x => x.productId).ToList();

            var urunler = await _context.Products
                .Where(p => idler.Contains(p.Id))
                .Select(p => new { p.Id, p.Name })
                .ToListAsync();

            var enCokSatanlar = satisOzeti.Select(x => new
            {
                urunId = x.productId,
                urunAdi = urunler.FirstOrDefault(u => u.Id == x.productId)?.Name ?? "Silinmiş ürün",
                adet = x.adet,
                ciro = x.ciro
            });

            // ---------- 4) KRİTİK STOK (5'ten az) ----------
            var kritikStok = await _context.Products
                .Where(p => p.Stock < 5)
                .OrderBy(p => p.Stock)
                .Take(10)
                .Select(p => new
                {
                    urunId = p.Id,
                    urunAdi = p.Name,
                    stok = p.Stock
                })
                .ToListAsync();

            // ---------- 5) SİPARİŞ DURUM DAĞILIMI ----------
            var durumDagilimi = await _context.Orders
                .GroupBy(o => o.Status)
                .Select(g => new
                {
                    durum = g.Key,
                    adet = g.Count()
                })
                .ToListAsync();

            // ---------- 6) SON 5 SİPARİŞ ----------
            var sonSiparisler = await _context.Orders
                .OrderByDescending(o => o.CreatedAt)
                .Take(5)
                .Join(_context.Users,
                      o => o.UserId,
                      u => u.Id,
                      (o, u) => new
                      {
                          id = o.Id,
                          musteri = u.FullName,
                          tutar = o.Total,
                          durum = o.Status,
                          odemeDurumu = o.PaymentStatus,
                          tarih = o.CreatedAt
                      })
                .ToListAsync();

            // ---------- HEPSİNİ TEK PAKETTE GÖNDER ----------
            return Ok(new
            {
                buAyGelir = buAyGelir,
                gecenAyGelir = gecenAyGelir,
                degisimYuzde = Math.Round(degisimYuzde, 1),
                buAySiparis = buAySiparis,

                gunlukGelir = gunlukGelir,
                enCokSatanlar = enCokSatanlar,
                kritikStok = kritikStok,
                durumDagilimi = durumDagilimi,
                sonSiparisler = sonSiparisler
            });
        }
    }
}