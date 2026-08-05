using ETicaretAPI.Data;
using ETicaretAPI.DTOs;
using ETicaretAPI.Models;
using ETicaretAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
namespace ETicaretAPI.Controllers
{
    [Route("api/admin")]
    [ApiController]
    [Authorize(Roles = "admin")] // TÜM admin controller'ı sadece admin'e açık
    public class AdminController : ControllerBase
    {
        private readonly AppDbContext _context;

        // ⭐ YENİ — gün sınırlarını yerel saate göre hesaplamak için.
        // Dashboard'daki "son 7 gün" grafiği UTC gününe göre
        // gruplandığı için Türkiye saatiyle gece 00:00–03:00 arası
        // siparişler bir ÖNCEKİ günün kutusuna düşüyordu.
        private readonly ETicaretAPI.Services.RaporTarihi _tarih;

        // ⭐ YENİ — karar bildirimi e-postası için
        private readonly ETicaretAPI.Services.IEmailGonderici _email;
        private readonly ETicaretAPI.Services.EmailSablonlari _sablonlar;
        private readonly ILogger<AdminController> _log;

        public AdminController(
            AppDbContext context,
            ETicaretAPI.Services.RaporTarihi tarih,
            ETicaretAPI.Services.IEmailGonderici email,          // ⭐ YENİ
            ETicaretAPI.Services.EmailSablonlari sablonlar,      // ⭐ YENİ
            ILogger<AdminController> log)                        // ⭐ YENİ
        {
            _context = context;
            _tarih = tarih;
            _email = email;                                       // ⭐ YENİ
            _sablonlar = sablonlar;                               // ⭐ YENİ
            _log = log;                                           // ⭐ YENİ
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
                    siparisNo = o.OrderNumber,   // ⭐
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
            // ⭐ DÜZELTİLDİ — gün sınırı artık YEREL saate göre.
            //
            // ESKİ HATA: simdi.Date UTC gününü alıyordu ve
            // p.PaidAt.Date de UTC gününe göre grupluyordu. Türkiye
            // UTC+3 olduğu için gece 00:00–03:00 arasında verilen her
            // sipariş grafikte bir ÖNCEKİ güne yazılıyordu.
            //
            // Hiçbir hata mesajı çıkmıyordu; grafik çiziliyordu, sayılar
            // makul görünüyordu. Sadece yanlıştı.
            //
            // RaporTarihi.Aralik() son 7 günü yerel gün sınırlarıyla
            // hesaplayıp UTC karşılığını veriyor — rapor uçlarıyla
            // birebir aynı mantık, tek yerden.
            var grafikAraligi = _tarih.Aralik(
                _tarih.YereleCevir(DateTime.UtcNow).Date.AddDays(-6),
                null);

            var hamOdemeler = await _context.Payments
                .Where(p => p.Status == "basarili"
                         && p.PaidAt >= grafikAraligi.BaslangicUtc
                         && p.PaidAt < grafikAraligi.BitisUtcHaric)
                .Select(p => new { p.PaidAt, p.Amount })
                .ToListAsync();

            // ⚠️ Gruplama neden BELLEKTE?
            // UTC → yerel çevrimini SQL'de yapamıyoruz; EF Core
            // TimeZoneInfo metodlarını SQL'e çeviremiyor. Filtre zaten
            // veriyi 7 güne indirdi, kalan iş bellekte ucuz.
            //
            // Ham veriyi bir kez çevirip listeye alıyoruz — döngünün
            // içinde çevirseydik her ödeme 7 kez çevrilirdi.
            var yerelOdemeler = hamOdemeler
                .Select(p => new
                {
                    Gun = _tarih.YereleCevir(p.PaidAt).Date,
                    p.Amount
                })
                .ToList();

            var gunlukGelir = new List<object>();

            for (int i = 0; i < 7; i++)
            {
                var gun = grafikAraligi.BaslangicYerel.AddDays(i);

                var toplam = yerelOdemeler
                    .Where(p => p.Gun == gun)
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
                          // id: React'in key prop'u ve ileride detay linki için gerekli
                          id = o.Id,

                          // ⭐ siparisNo: EKRANDA GÖSTERİLEN numara.
                          // Teknik anahtar (Id) ile iş anahtarı (OrderNumber) ayrımı —
                          // Aşama 2'de koyduğumuz kural. Id iç kullanım, OrderNumber
                          // müşteriyle konuşulan numara.
                          siparisNo = o.OrderNumber,
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


        // ============================================================
        //  🟣 GET /api/admin/audit-logs
        //     ?arama=&islem=&baslangic=&bitis=&page=1&pageSize=20
        //
        //  DENETİM KAYDI — "kim, kimi, ne zaman, ne yaptı?"
        //
        //  NEDEN SADECE SÜPERADMİN?
        //  Bu tablo ADMİNLERİN ne yaptığını gösteriyor. Adminin
        //  başkalarının izini görebilmesi, denetlenen kişinin
        //  denetim mekanizmasına erişmesi demektir — ve kendi izini
        //  kimin takip ettiğini öğrenir.
        //
        //  Denetim mekanizmasının denetlenenden ayrı tutulması
        //  temel bir güvenlik ilkesidir.
        // ============================================================
        [Authorize(Roles = "superadmin")]
        [HttpGet("audit-logs")]
        public async Task<IActionResult> GetAuditLogs(
            [FromQuery] string? arama,
            [FromQuery] string? islem,
            [FromQuery] DateTime? baslangic,
            [FromQuery] DateTime? bitis,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            // Sayfalama parametrelerini güvenli aralığa çek.
            //
            // Neden hata döndürmüyoruz? Bunlar kullanıcının elle
            // yazdığı değerler değil, arayüzün gönderdiği teknik
            // parametreler. Bozuk gelirse makul bir varsayılana
            // düşmek, hata ekranı göstermekten iyi.
            //
            // pageSize'a ÜST SINIR şart: 999999 gönderen bir istek
            // tüm tabloyu belleğe çeker — bu bir servis dışı bırakma
            // (DoS) kapısıdır.
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            // ⭐ Aşama 2'de yazdığımız RaporTarihi servisini
            // yeniden kullanıyoruz.
            //
            // Gün sınırı yerel saate göre hesaplanıyor: Türkiye'de
            // gece 01:00'da yapılan bir rol değişikliği, UTC'de bir
            // önceki güne düşer. Raporlarda çözdüğümüz problemin
            // aynısı — çözümü de aynı, çünkü tek yerde yaşıyor.
            var aralik = _tarih.Aralik(baslangic, bitis);

            // ⚠️ IQueryable: sorgu henüz veritabanına GİTMEDİ.
            // Aşağıda koşullu olarak filtre ekleyeceğiz, hepsi
            // TEK SQL'e derlenecek.
            //
            // List olsaydı tüm tabloyu belleğe çekip orada elerdik.
            var sorgu = _context.AuditLogs
                .Where(l => l.CreatedAt >= aralik.BaslangicUtc
                         && l.CreatedAt < aralik.BitisUtcHaric);

            // ---- İŞLEM TİPİ FİLTRESİ ----
            if (!string.IsNullOrWhiteSpace(islem))
            {
                sorgu = sorgu.Where(l => l.Action == islem);
            }

            // ---- İSİM ARAMASI ----
            //
            // Hem işlemi YAPAN hem işlem YAPILAN kişide arıyoruz.
            //
            // Neden ikisi birden? Kullanıcı "Ahmet" yazdığında ne
            // kastettiğini bilemeyiz: "Ahmet ne yaptı" mı, "Ahmet'e
            // ne yapıldı" mı? İkisini de döndürmek, iki ayrı arama
            // kutusu koymaktan daha kullanışlı.
            if (!string.IsNullOrWhiteSpace(arama))
            {
                var a = arama.Trim();

                sorgu = sorgu.Where(l =>
                    l.ActorName.Contains(a) || l.TargetName.Contains(a));
            }

            // ⚠️ TOPLAM SAYIYI FİLTRELERDEN SONRA, SAYFALAMADAN
            // ÖNCE alıyoruz.
            //
            // Sayfalamadan sonra alsaydık her zaman en fazla
            // pageSize kadar çıkardı ve "toplam 340 kayıt" bilgisi
            // yanlış olurdu.
            var toplam = await sorgu.CountAsync();

            var loglar = await sorgu
                // En yeni üstte — denetim kaydına bakan kişi
                // "en son ne oldu" sorusuyla gelir.
                .OrderByDescending(l => l.CreatedAt)

                // Skip + Take = sayfalama. SQL'de OFFSET/FETCH olur.
                .Skip((page - 1) * pageSize)
                .Take(pageSize)

                .Select(l => new
                {
                    l.Id,

                    yapanId = l.ActorUserId,
                    yapan = l.ActorName,

                    hedefId = l.TargetUserId,
                    hedef = l.TargetName,

                    islem = l.Action,
                    eski = l.OldValue,
                    yeni = l.NewValue,

                    tarih = l.CreatedAt
                })
                .ToListAsync();

            // ---- MEVCUT İŞLEM TİPLERİ ----
            //
            // NEDEN VERİTABANINDAN OKUYORUZ, NEDEN SABİT LİSTE DEĞİL?
            //
            // Sabit liste yazsaydık, backend'e yeni bir Action
            // eklendiğinde (örneğin dün eklediğimiz
            // "yorum_gizlendi") filtre listesini güncellemek
            // unutulurdu ve o işlem tipi hiç filtrelenemezdi.
            //
            // Veritabanından okuyunca liste kendiliğinden büyüyor.
            // Bedeli tek bir DISTINCT sorgusu.
            //
            // ⚠️ Bu sorgu FİLTRELERDEN BAĞIMSIZ (sorgu değişkenini
            // kullanmıyor). Sebep: kullanıcı "rol_degisti" filtresi
            // uygulamışken açılır menüde sadece o seçenek kalsaydı
            // başka bir tipe geçemezdi — kendi kendini kilitleyen
            // bir filtre olurdu.
            var islemTipleri = await _context.AuditLogs
                .Select(l => l.Action)
                .Distinct()
                .OrderBy(a => a)
                .ToListAsync();

            return Ok(new
            {
                loglar,
                toplam,
                sayfa = page,
                sayfaBoyutu = pageSize,
                toplamSayfa = (int)Math.Ceiling(toplam / (double)pageSize),

                // Dönemi geri gönderiyoruz — kullanıcı tarih
                // seçmediyse hangi aralığı gördüğünü bilmeli.
                baslangic = aralik.BaslangicYerel.ToString("yyyy-MM-dd"),
                bitis = aralik.BitisYerel.ToString("yyyy-MM-dd"),

                islemTipleri
            });
        }



        // 🔴 GET /api/admin/dikkat-gerektirenler?gunEsigi=3
        //
        // Adminin "acilen bakmam gereken ne var?" sorusuna cevap.
        //
        // TASARIM: Her uyarı türü AYNI şekilde dönüyor:
        //   { tur, baslik, adet, oncelik, uyariOgeleri[] }
        // Öğeler de aynı kalıpta: { metin, altMetin, sagMetin, link }
        // Böylece ekran tek bir şablonla hepsini çizer; yeni uyarı türü
        // eklendiğinde EKRAN HİÇ DEĞİŞMEZ.
        [HttpGet("dikkat-gerektirenler")]
        public async Task<IActionResult> GetDikkatGerektirenler(int gunEsigi = 3)
        {
            var uyarilar = new List<object>();
            var simdi = DateTime.UtcNow;

            // ---------- 1) UZUN SÜREDİR HAZIRLANIYOR ----------
            var esikTarihi = simdi.AddDays(-gunEsigi);

            var bekleyenler = await _context.Orders
                .Where(o => o.Status == "hazirlaniyor" && o.CreatedAt < esikTarihi)
                .OrderBy(o => o.CreatedAt)            // en eski = en acil, üstte
                .Join(_context.Users,
                      o => o.UserId,
                      u => u.Id,
                      (o, u) => new { o, u })
                .Select(x => new
                {
                    metin = x.o.OrderNumber,          // SP-260713-0002
                    altMetin = x.u.FullName,          // Müşteri adı
                    tarih = x.o.CreatedAt,            // "kaç gündür" ekranda hesaplanacak
                    tutar = x.o.Total,
                    link = "/siparisler/" + x.o.Id    // tıklayınca gidilecek yer
                })
                .Take(8)
                .ToListAsync();

            if (bekleyenler.Count > 0)
            {
                uyarilar.Add(new
                {
                    tur = "bekleyen_siparis",
                    baslik = "Bekleyen sipariş",
                    ozet = bekleyenler.Count + " sipariş " + gunEsigi + "+ gündür hazırlanıyor",
                    adet = bekleyenler.Count,
                    oncelik = "yuksek",
                    tumunuGorLink = "/siparisler",
                    ogeler = bekleyenler
                });
            }

            // ---------- 2) KRİTİK STOK ----------
            var kritikStok = await _context.Products
                .Where(p => p.Stock < 5)
                .OrderBy(p => p.Stock)                // en azı en üstte
                .Join(_context.Categories,
                      p => p.CategoryId,
                      c => c.Id,
                      (p, c) => new { p, c })
                .Select(x => new
                {
                    metin = x.p.Name,
                    altMetin = x.c.Name,              // kategori adı
                    tarih = (DateTime?)null,          // stok için tarih yok
                    tutar = (decimal?)null,
                    stok = x.p.Stock,                 // sağda "3 adet" diye görünecek
                    link = "/urunler/" + x.p.Id + "/duzenle"
                })
                .Take(8)
                .ToListAsync();

            if (kritikStok.Count > 0)
            {
                var tukenen = kritikStok.Count(k => k.stok == 0);

                uyarilar.Add(new
                {
                    tur = "kritik_stok",
                    baslik = "Kritik stok",
                    ozet = tukenen > 0
                        ? kritikStok.Count + " üründe stok azaldı (" + tukenen + " tanesi tükendi)"
                        : kritikStok.Count + " üründe stok azaldı",
                    adet = kritikStok.Count,
                    oncelik = tukenen > 0 ? "yuksek" : "orta",
                    tumunuGorLink = "/urunler",
                    ogeler = kritikStok
                });
            }

            // ---------- İLERİDE EKLENECEKLER ----------
            // Aşama 7  → bekleyen admin başvuruları (sadece superadmin)
            // Aşama 11 → cevaplanmamış destek talepleri
            // Aşama 12 → bekleyen iade talepleri

            return Ok(new { uyarilar = uyarilar });
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


        // ⭐ YENİ — ROL DEĞİŞTİRME: ORTAK ADIMLAR
        //
        // NEDEN BU METOT VAR?
        //
        // Rol değiştirmek tek satırlık bir iş DEĞİL. Dört adımı var
        // ve dördü de atlanırsa güvenlik açığı doğar:
        //
        //   1) Rolü yaz
        //   2) SecurityStamp'i yenile → elindeki access token'lar
        //      anında geçersiz olur
        //   3) Refresh token'ları iptal et → yenileme yoluyla
        //      oturumunu sürdüremesin
        //   4) Denetim kaydı yaz → "bu kişiyi kim admin yaptı?"
        //
        // Bu dört adım şu an ChangeUserRole'de yazılı. Başvuru
        // onaylama da AYNI dördünü yapacak.
        //
        // Kopyalasaydık: yarın beşinci bir adım eklendiğinde (mesela
        // "bildirim gönder") birini güncelleyip diğerini unutmak
        // işten değildi — ve unutulan taraf SESSİZCE eksik çalışırdı.
        //
        // "İki yerde yazılan gerçek er ya da geç ikiye ayrılır."
        //
        // ⚠️ SaveChanges ÇAĞIRMIYOR — bilerek. Çağıran, kendi
        // işlemiyle aynı SaveChanges'te yazsın. StokDefteri'ndeki
        // desenin aynısı.
        private async Task RolDegistirAsync(User user, string yeniRol, string islemAdi)
        {
            var eskiRol = user.Role;

            user.Role = yeniRol;

            // Damgayı yenile — elindeki TÜM access token'lar geçersiz.
            user.SecurityStamp = Guid.NewGuid().ToString();

            // ⭐ Refresh token'ları da iptal et.
            //
            // Neden gerekli? SecurityStamp sadece ACCESS token'ı
            // öldürür. Elinde geçerli bir refresh token kalırsa
            // 15 dakika sonra yenileyip yeni bir access token alır
            // ve oturumu hiç kesilmemiş gibi devam eder.
            //
            // ExecuteUpdateAsync: tek SQL cümlesi, satırları belleğe
            // çekmeden günceller. 30 oturumu olan bir kullanıcıda
            // 30 nesne yüklemekten çok daha ucuz.
            //
            // ⚠️ ExecuteUpdateAsync change tracker'ı ATLAR ve
            // ANINDA çalışır — SaveChanges beklemez. Burada bu
            // sorun değil: token iptali geri alınsa bile kullanıcı
            // sadece yeniden giriş yapmak zorunda kalır, veri
            // bozulmaz.
            await _context.RefreshTokens
                .Where(t => t.UserId == user.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));

            await LogEkle(user.Id, user.FullName, islemAdi, eskiRol, yeniRol);
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

            // ⭐ DEĞİŞTİ — dört adım artık ortak metotta.
            await RolDegistirAsync(user, yeniRol, "rol_degisti");

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

        // ==========================================================
        //  ADMİN BAŞVURULARI — SADECE SÜPER ADMİN
        // ==========================================================

        // 🟣 GET /api/admin/basvurular?durum=beklemede&page=1&pageSize=10
        [Authorize(Roles = "superadmin")]
        [HttpGet("basvurular")]
        public async Task<IActionResult> GetBasvurular(
            [FromQuery] string? durum,
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

            var sorgu = _context.AdminBasvurular.AsQueryable();

            if (!string.IsNullOrWhiteSpace(durum))
            {
                sorgu = sorgu.Where(b => b.Durum == durum);
            }

            // Toplam FİLTRELERDEN SONRA, sayfalamadan ÖNCE.
            var toplam = await sorgu.CountAsync();

            // Bekleyen sayısı filtreden BAĞIMSIZ hesaplanıyor:
            // ekran "Karar Verilenler" sekmesindeyken bile üstteki
            // rozet kaç başvurunun beklediğini göstermeli.
            var bekleyenSayisi = await _context.AdminBasvurular
                .CountAsync(b => b.Durum == BasvuruDurumu.Beklemede);

            var basvurular = await sorgu
                // Bekleyenlerde en ESKİ üstte olmalı — en uzun
                // bekleyen en acil olandır. (Sipariş listesindeki
                // "en yeni üstte" mantığının tersi; soru farklı.)
                .OrderBy(b => b.CreatedAt)

                // Eşit CreatedAt değerlerinde sıra garanti olsun.
                .ThenBy(b => b.Id)

                .Skip((page - 1) * pageSize)
                .Take(pageSize)

                .Select(b => new
                {
                    id = b.Id,
                    gerekce = b.Gerekce,
                    durum = b.Durum,
                    tarih = b.CreatedAt,

                    // ⚠️ ALT SORGU — JOIN DEĞİL.
                    //
                    // Başvuran kullanıcı normalde silinmez ama
                    // hesabını kapatmış olabilir (anonimleştirme).
                    // INNER JOIN o başvuruyu listeden komple
                    // düşürürdü ve sayaçla liste tutmazdı.
                    basvuranId = b.UserId,

                    basvuran = _context.Users
                        .Where(u => u.Id == b.UserId)
                        .Select(u => u.FullName)
                        .FirstOrDefault(),

                    basvuranEmail = _context.Users
                        .Where(u => u.Id == b.UserId)
                        .Select(u => u.Email)
                        .FirstOrDefault(),

                    kararTarihi = b.KararTarihi,
                    redNedeni = b.RedNedeni,

                    kararVeren = _context.Users
                        .Where(u => u.Id == b.KararVerenUserId)
                        .Select(u => u.FullName)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var toplamSayfa = (int)Math.Ceiling(toplam / (double)pageSize);

            return Ok(new
            {
                basvurular = basvurular,
                bekleyenSayisi = bekleyenSayisi,
                toplam = toplam,
                sayfa = page,
                sayfaBoyutu = pageSize,
                toplamSayfa = toplamSayfa
            });
        }


        // 🟣 PUT /api/admin/basvurular/5/onayla
        [Authorize(Roles = "superadmin")]
        [HttpPut("basvurular/{id}/onayla")]
        public async Task<IActionResult> BasvuruOnayla(int id)
        {
            var basvuru = await _context.AdminBasvurular.FindAsync(id);

            if (basvuru == null)
            {
                return NotFound(new { mesaj = "Başvuru bulunamadı!" });
            }

            // ⚠️ İDEMPOTENTLİK — ikinci çağrı HATA DEĞİL BAŞARI döner.
            //
            // "Durum değiştiren uçlar PUT ve idempotent olmalı."
            // Süperadmin çift tıklarsa veya sayfa yenilenirse ikinci
            // istek gelir. Hata dönseydi ekranda kırmızı bir uyarı
            // çıkardı — ama aslında iş TAMAMLANMIŞTI.
            if (basvuru.Durum == BasvuruDurumu.Onaylandi)
            {
                return Ok(new { mesaj = "Bu başvuru zaten onaylanmış." });
            }

            if (basvuru.Durum == BasvuruDurumu.Reddedildi)
            {
                return BadRequest(new
                {
                    mesaj = "Reddedilmiş bir başvuru onaylanamaz. Kullanıcının rolünü kullanıcılar sayfasından değiştirebilirsin."
                });
            }

            var kullanici = await _context.Users.FindAsync(basvuru.UserId);

            if (kullanici == null)
            {
                return BadRequest(new { mesaj = "Başvuran kullanıcı artık sistemde yok." });
            }

            // ⚠️ SÜPERADMİN BAŞVURUYLA VERİLEMEZ.
            //
            // Bu uç sadece "admin" atıyor — "superadmin" hiçbir
            // koşulda buradan verilemez. Sistemin kök yetkisi
            // uygulamanın içinden üretilemez (bootstrap kuralı,
            // AtanabilirRoller whitelist'iyle aynı gerekçe).
            if (kullanici.Role != "customer")
            {
                return BadRequest(new
                {
                    mesaj = $"{kullanici.FullName} zaten '{kullanici.Role}' rolünde."
                });
            }

            // Dört adım tek çağrıda: rol + damga + token iptali + log
            await RolDegistirAsync(kullanici, "admin", "basvuru_onaylandi");

            basvuru.Durum = BasvuruDurumu.Onaylandi;
            basvuru.KararVerenUserId = IstekYapanId();
            basvuru.KararTarihi = DateTime.UtcNow;

            // Tek SaveChanges: kullanıcı, başvuru ve denetim kaydı
            // birlikte yazılıyor. Ya hepsi ya hiçbiri.
            await _context.SaveChangesAsync();

            // ⚠️ MAİL SaveChanges'TEN SONRA.
            // Öncesinde gönderseydik ve kayıt patlasaydı, kullanıcıya
            // "admin oldun" maili gitmiş ama olmamış olurdu.
            // "Geri alınamaz yan etkiler, geri alınabilir olanların
            // sonrasına konur."
            await _email.GuvenliGonderAsync(
                _log,
                kullanici.Email,
                _sablonlar.BasvuruOnaylandi(kullanici.FullName),
                "BasvuruOnaylandi");

            return Ok(new
            {
                mesaj = $"{kullanici.FullName} artık yönetici. Mevcut oturumları sonlandırıldı."
            });
        }


        // 🟣 PUT /api/admin/basvurular/5/reddet
        //
        // Neden PUT, neden POST değil? Bu bir DURUM GEÇİŞİ, yeni bir
        // kaynak oluşturma değil. Gövde taşıması sorun değil —
        // gövdesi tanımsız olan HTTP metodu DELETE'tir, PUT değil.
        [Authorize(Roles = "superadmin")]
        [HttpPut("basvurular/{id}/reddet")]
        public async Task<IActionResult> BasvuruReddet(int id, [FromBody] BasvuruRedDto dto)
        {
            var basvuru = await _context.AdminBasvurular.FindAsync(id);

            if (basvuru == null)
            {
                return NotFound(new { mesaj = "Başvuru bulunamadı!" });
            }

            if (basvuru.Durum == BasvuruDurumu.Reddedildi)
            {
                return Ok(new { mesaj = "Bu başvuru zaten reddedilmiş." });
            }

            if (basvuru.Durum == BasvuruDurumu.Onaylandi)
            {
                return BadRequest(new
                {
                    mesaj = "Onaylanmış bir başvuru reddedilemez. Yetkiyi geri almak için kullanıcılar sayfasından rolü değiştir."
                });
            }

            var kullanici = await _context.Users.FindAsync(basvuru.UserId);

            basvuru.Durum = BasvuruDurumu.Reddedildi;
            basvuru.RedNedeni = dto.RedNedeni.Trim();
            basvuru.KararVerenUserId = IstekYapanId();
            basvuru.KararTarihi = DateTime.UtcNow;

            // Denetim kaydı: rol değişmediği için RolDegistirAsync
            // kullanmıyoruz, sadece log yazıyoruz.
            //
            // Eski/yeni değer olarak durumu geçiyoruz — denetim
            // ekranındaki "eski → yeni" sütunu anlamlı kalsın.
            await LogEkle(
                basvuru.UserId,
                kullanici?.FullName ?? "Bilinmeyen",
                "basvuru_reddedildi",
                BasvuruDurumu.Beklemede,
                BasvuruDurumu.Reddedildi);

            await _context.SaveChangesAsync();

            if (kullanici != null)
            {
                await _email.GuvenliGonderAsync(
                    _log,
                    kullanici.Email,
                    _sablonlar.BasvuruReddedildi(kullanici.FullName, basvuru.RedNedeni),
                    "BasvuruReddedildi");
            }

            return Ok(new { mesaj = "Başvuru reddedildi." });
        }

    }
}