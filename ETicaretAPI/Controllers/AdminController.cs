using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;
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

        // 🔴 GET /api/admin/users
        //     ?search=&role=&sortBy=harcama&page=1&pageSize=10
        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers(
            [FromQuery] string? search,
            [FromQuery] string? role,
            [FromQuery] string? sortBy,
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

            var query = _context.Users.AsQueryable();

            // --- FİLTRELER ---
            if (!string.IsNullOrWhiteSpace(search))
            {
                var arama = search.Trim();

                query = query.Where(u =>
                    u.FullName.Contains(arama) ||
                    u.Email.Contains(arama));
            }

            if (!string.IsNullOrWhiteSpace(role))
            {
                query = query.Where(u => u.Role == role);
            }

            var toplam = await query.CountAsync();

            // --- HER MÜŞTERİNİN İSTATİSTİĞİNİ HESAPLA ---
            // Not: Bunları SQL'in içinde alt sorgu olarak yazıyoruz.
            // Alternatif (yanlış) yol: kullanıcıları çek, her biri için ayrı
            // sipariş sorgusu at → 100 kullanıcı = 201 sorgu (N+1 problemi).
            var temel = query.Select(u => new
            {
                u.Id,
                u.FullName,
                u.Email,
                u.Role,
                u.CreatedAt,
                u.IsActive,

                siparisSayisi = _context.Orders.Count(o => o.UserId == u.Id),

                // Harcama = başarılı ödemeler (iadeler HARİÇ)
                toplamHarcama = _context.Payments
                    .Where(p => p.UserId == u.Id && p.Status == "basarili")
                    .Sum(p => (decimal?)p.Amount) ?? 0,

                sonSiparisTarihi = _context.Orders
                    .Where(o => o.UserId == u.Id)
                    .OrderByDescending(o => o.CreatedAt)
                    .Select(o => (DateTime?)o.CreatedAt)
                    .FirstOrDefault()
            });

            // --- SIRALAMA ---
            // Whitelist mantığı: tanımadığımız sortBy değeri gelirse
            // varsayılana düşüyoruz. Ham metni SQL'e sokmuyoruz.
            temel = sortBy switch
            {
                "harcama" => temel.OrderByDescending(u => u.toplamHarcama),
                "siparis" => temel.OrderByDescending(u => u.siparisSayisi),
                "eski" => temel.OrderBy(u => u.CreatedAt),
                "isim" => temel.OrderBy(u => u.FullName),
                _ => temel.OrderByDescending(u => u.CreatedAt) // varsayılan: en yeni
            };

            var kullanicilar = await temel
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // --- ÖZET (filtreye uyan TÜM kullanıcılar) ---
            var musteriSayisi = await query.CountAsync(u => u.Role == "customer");
            var adminSayisi = await query.CountAsync(u => u.Role == "admin");

            var buAyBasi = new DateTime(
                DateTime.UtcNow.Year,
                DateTime.UtcNow.Month,
                1, 0, 0, 0, DateTimeKind.Utc);

            var buAyYeni = await query.CountAsync(u => u.CreatedAt >= buAyBasi);

            var toplamSayfa = (int)Math.Ceiling(toplam / (double)pageSize);

            return Ok(new
            {
                kullanicilar = kullanicilar,
                toplam = toplam,
                sayfa = page,
                sayfaBoyutu = pageSize,
                toplamSayfa = toplamSayfa,

                ozet = new
                {
                    musteriSayisi = musteriSayisi,
                    adminSayisi = adminSayisi,
                    buAyYeni = buAyYeni
                }
            });
        }

        // 🔴 GET /api/admin/users/5 — müşteri detayı
        [HttpGet("users/{id}")]
        public async Task<IActionResult> GetUserDetail(int id)
        {
            var user = await _context.Users.FindAsync(id);

            if (user == null)
            {
                return NotFound(new { mesaj = "Kullanıcı bulunamadı!" });
            }

            // Siparişleri
            var siparisler = await _context.Orders
                .Where(o => o.UserId == id)
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new
                {
                    id = o.Id,
                    tutar = o.Total,
                    durum = o.Status,
                    odemeDurumu = o.PaymentStatus,
                    tarih = o.CreatedAt,
                    urunCesidi = _context.OrderItems.Count(oi => oi.OrderId == o.Id)
                })
                .ToListAsync();

            // Adresleri
            var adresler = await _context.Addresses
                .Where(a => a.UserId == id)
                .Select(a => new { a.Id, a.Title, a.City, a.FullAddress })
                .ToListAsync();

            // Kayıtlı kartları — SADECE son 4 hane.
            // Tam numara ve CVV zaten veritabanında YOK, olsaydı da göndermezdik.
            var kartlar = await _context.Cards
                .Where(c => c.UserId == id)
                .Select(c => new { c.Id, c.CardHolderName, c.Last4Digits })
                .ToListAsync();

            // Harcama özeti
            var brutHarcama = await _context.Payments
                .Where(p => p.UserId == id && p.Status == "basarili")
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            var iadeToplam = await _context.Payments
                .Where(p => p.UserId == id && p.Status == "iade")
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            // En çok aldığı ürünler (ilk 5)
            var favoriUrunler = await _context.OrderItems
                .Where(oi => _context.Orders
                    .Any(o => o.Id == oi.OrderId && o.UserId == id))
                .GroupBy(oi => oi.ProductId)
                .Select(g => new
                {
                    productId = g.Key,
                    adet = g.Sum(x => x.Quantity)
                })
                .OrderByDescending(x => x.adet)
                .Take(5)
                .ToListAsync();

            var urunIdleri = favoriUrunler.Select(x => x.productId).ToList();

            var urunAdlari = await _context.Products
                .Where(p => urunIdleri.Contains(p.Id))
                .Select(p => new { p.Id, p.Name })
                .ToListAsync();

            var enCokAldiklari = favoriUrunler.Select(x => new
            {
                urunId = x.productId,
                urunAdi = urunAdlari.FirstOrDefault(u => u.Id == x.productId)?.Name ?? "Silinmiş ürün",
                adet = x.adet
            });

            return Ok(new
            {
                id = user.Id,
                adSoyad = user.FullName,
                email = user.Email,
                rol = user.Role,
                kayitTarihi = user.CreatedAt,
                aktifMi = user.IsActive,
                // ⚠️ PasswordHash ASLA gönderilmiyor

                ozet = new
                {
                    siparisSayisi = siparisler.Count,
                    brutHarcama = brutHarcama,
                    iadeToplam = iadeToplam,
                    netHarcama = brutHarcama - iadeToplam,

                    ortalamaSepet = siparisler.Count > 0
                        ? Math.Round(brutHarcama / siparisler.Count, 2)
                        : 0,

                    adresSayisi = adresler.Count,
                    kartSayisi = kartlar.Count
                },

                siparisler = siparisler,
                adresler = adresler,
                kartlar = kartlar,
                loglar = await _context.AuditLogs
                    .Where(l => l.TargetUserId == id)
                    .OrderByDescending(l => l.CreatedAt)
                    .Take(20)
                    .Select(l => new
                    {
                        l.Id,
                        yapan = l.ActorName,
                        islem = l.Action,
                        eski = l.OldValue,
                        yeni = l.NewValue,
                        tarih = l.CreatedAt
                    })
                    .ToListAsync(),
                enCokAldiklari = enCokAldiklari
            });
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

        // ==========================================================
        //  KULLANICI YÖNETİMİ — SADECE SÜPER ADMİN
        // ==========================================================

        // Panelden verilebilecek roller — WHITELIST.
        // 'superadmin' bu listede YOK ve olmayacak:
        // sistemin kök yetkisi uygulamanın içinden üretilemez (bootstrap kuralı).
        private static readonly string[] AtanabilirRoller = { "customer", "admin" };

        // İsteği yapan kişinin id'sini token'dan al
        private int IstekYapanId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // Log kaydı oluştur (kaydetmez, sadece ekler — SaveChanges çağıran sorumlu)
        private async Task LogEkle(int hedefId, string hedefAd, string islem, string? eski, string? yeni)
        {
            var yapanId = IstekYapanId();

            var yapan = await _context.Users
                .Where(u => u.Id == yapanId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync() ?? "Bilinmeyen";

            _context.AuditLogs.Add(new Models.AuditLog
            {
                ActorUserId = yapanId,
                ActorName = yapan,
                TargetUserId = hedefId,
                TargetName = hedefAd,
                Action = islem,
                OldValue = eski,
                NewValue = yeni,
                CreatedAt = DateTime.UtcNow
            });
        }

        // 🟣 PUT /api/admin/users/5/role — kullanıcının rolünü değiştir
        [Authorize(Roles = "superadmin")]  // ⭐ admin YETMEZ, süper admin şart
        [HttpPut("users/{id}/role")]
        public async Task<IActionResult> ChangeUserRole(int id, [FromBody] RoleUpdateDto dto)
        {
            var yeniRol = dto.Role.Trim().ToLowerInvariant();

            // KURAL 1: Rol whitelist'ten seçilir
            if (!AtanabilirRoller.Contains(yeniRol))
            {
                return BadRequest(new
                {
                    mesaj = "Geçersiz rol! Sadece şunlar atanabilir: " +
                            string.Join(", ", AtanabilirRoller)
                });
            }

            // KURAL 2: Kimse KENDİ rolünü değiştiremez
            // (Yoksa tek süper admin kendini müşteri yapar, sisteme kimse giremez.)
            if (id == IstekYapanId())
            {
                return BadRequest(new { mesaj = "Kendi rolünü değiştiremezsin!" });
            }

            var user = await _context.Users.FindAsync(id);

            if (user == null)
            {
                return NotFound(new { mesaj = "Kullanıcı bulunamadı!" });
            }

            // KURAL 3: Süper admin'e dokunulamaz
            if (user.Role == "superadmin")
            {
                return BadRequest(new
                {
                    mesaj = "Süper yöneticinin rolü panelden değiştirilemez."
                });
            }

            if (user.Role == yeniRol)
            {
                return Ok(new { mesaj = "Kullanıcı zaten bu rolde." });
            }

            var eskiRol = user.Role;

            user.Role = yeniRol;

            // ⭐ DAMGAYI YENİLE — bu kişinin ELİNDEKİ TÜM TOKEN'LAR anında geçersiz olur.
            // Yetkisi düşürülen biri, eski token'ıyla admin gibi davranamaz.
            user.SecurityStamp = Guid.NewGuid().ToString();

            await LogEkle(user.Id, user.FullName, "rol_degisti", eskiRol, yeniRol);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                mesaj = $"{user.FullName} artık '{yeniRol}' rolünde. Mevcut oturumu sonlandırıldı.",
                rol = yeniRol
            });
        }

        // 🟣 PUT /api/admin/users/5/status — aktifleştir / pasifleştir
        [Authorize(Roles = "superadmin")]
        [HttpPut("users/{id}/status")]
        public async Task<IActionResult> ChangeUserStatus(int id, [FromBody] StatusToggleDto dto)
        {
            // KURAL: Kimse kendini pasifleştiremez
            if (id == IstekYapanId())
            {
                return BadRequest(new { mesaj = "Kendi hesabını devre dışı bırakamazsın!" });
            }

            var user = await _context.Users.FindAsync(id);

            if (user == null)
            {
                return NotFound(new { mesaj = "Kullanıcı bulunamadı!" });
            }

            // KURAL: Süper admin pasifleştirilemez
            if (user.Role == "superadmin")
            {
                return BadRequest(new
                {
                    mesaj = "Süper yönetici devre dışı bırakılamaz."
                });
            }

            if (user.IsActive == dto.IsActive)
            {
                return Ok(new { mesaj = "Durum zaten aynı." });
            }

            var eski = user.IsActive ? "aktif" : "pasif";
            var yeni = dto.IsActive ? "aktif" : "pasif";

            user.IsActive = dto.IsActive;

            // Pasifleştirirken damgayı yenile → anında sistemden atılır
            if (!dto.IsActive)
            {
                user.SecurityStamp = Guid.NewGuid().ToString();
            }

            await LogEkle(
                user.Id,
                user.FullName,
                dto.IsActive ? "aktiflestirildi" : "pasiflestirildi",
                eski,
                yeni);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                mesaj = dto.IsActive
                    ? $"{user.FullName} yeniden aktifleştirildi."
                    : $"{user.FullName} devre dışı bırakıldı ve oturumu sonlandırıldı.",
                aktifMi = dto.IsActive
            });
        }

    }
}