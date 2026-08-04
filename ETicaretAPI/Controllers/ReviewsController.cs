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
                // ⭐ YENİ — gizlenmiş yorumlar müşteriye gösterilmez.
                //
                // Neden filtreyi buraya, sorguya koyuyoruz?
                // Alternatif, hepsini çekip bellekte elemekti. Yanlış
                // olurdu: gizli yorum ağdan geçmese bile veritabanından
                // gelmiş olurdu ve ileride sayfalama eklendiğinde
                // "10 yorum iste, 7 tane gel" gibi tuhaflıklar çıkardı.
                //
                // Filtre ne kadar erken uygulanırsa o kadar iyi:
                // veritabanı > bellek > ekran.
                .Where(r => r.ProductId == productId && !r.IsHidden)
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


        // ============================================================
        //  🔴 PUT /api/admin/reviews/5/gizle
        //  🔴 PUT /api/admin/reviews/5/goster
        //
        //  NEDEN ROTA MUTLAK (başında "/" var)?
        //  Bu controller'ın sınıf rotası "api/products/{productId}/reviews".
        //  Metot rotasına düz "gizle" yazsaydık adres şöyle olurdu:
        //      /api/products/{productId}/reviews/gizle
        //  Yani admin, yorumu gizlemek için ürün id'sini de bilmek
        //  zorunda kalırdı — oysa yorum id'si zaten tekil.
        //
        //  Başına "/" koyunca ASP.NET sınıf rotasını yok sayıp adresi
        //  kökten alıyor. Bu deseni OrdersController'da da kullandık
        //  (/api/admin/orders/{id}) — müşteri ve admin uçları aynı
        //  dosyada, farklı adres ağacında yaşıyor.
        //
        //  NEDEN PUT, POST DEĞİL?
        //  Bu bir DURUM DEĞİŞTİRME. Aynı isteği iki kez göndermek aynı
        //  sonucu verir (idempotent) — PUT'un tanımı budur. POST "yeni
        //  bir şey yarat" demektir, burada yaratılan bir şey yok.
        //
        //  NEDEN DELETE DEĞİL?
        //  Kaydı silmiyoruz; zaten tüm mesele silmemek.
        // ============================================================
        [Authorize(Roles = "admin")]
        [HttpPut("/api/admin/reviews/{id}/gizle")]
        public async Task<IActionResult> YorumuGizle(int id)
        {
            return await GorunurlukDegistir(id, gizle: true);
        }

        [Authorize(Roles = "admin")]
        [HttpPut("/api/admin/reviews/{id}/goster")]
        public async Task<IActionResult> YorumuGoster(int id)
        {
            return await GorunurlukDegistir(id, gizle: false);
        }


        // ------------------------------------------------------------
        //  İki ucun ORTAK gövdesi.
        //
        //  Neden tek metot? Gizleme ile gösterme arasındaki tek fark
        //  bir bool. İki ayrı metot yazsaydık denetim kaydı, hata
        //  yönetimi ve kullanıcı bulma kodu iki kez yazılırdı; birinde
        //  yapılan düzeltme diğerine unutulurdu.
        //
        //  Ama URL'ler AYRI kaldı. Neden?
        //  "/gorunurluk?gizle=true" gibi tek bir uç yazsaydık, çağıran
        //  taraf parametreyi yanlış göndererek ters işlem yapabilirdi.
        //  Niyet adreste açıkça yazılı olsun: uç ayrı, gövde ortak.
        // ------------------------------------------------------------
        private async Task<IActionResult> GorunurlukDegistir(int id, bool gizle)
        {
            var yorum = await _context.Reviews.FindAsync(id);

            if (yorum == null)
            {
                return NotFound(new { mesaj = "Yorum bulunamadı." });
            }

            // Zaten istenen durumdaysa boşuna yazma.
            //
            // Neden hata değil, başarı dönüyoruz? İşin SONUCU istenen
            // durum — o sağlanmış. İki sekmede aynı butona basan admin
            // hata mesajı görmemeli. (İdempotentlik böyle davranır.)
            if (yorum.IsHidden == gizle)
            {
                return Ok(new
                {
                    mesaj = gizle ? "Yorum zaten gizli." : "Yorum zaten görünür.",
                    gizli = yorum.IsHidden
                });
            }

            yorum.IsHidden = gizle;

            // ---- DENETİM KAYDI ----
            //
            // Neden kayıt tutuyoruz? Gizlemenin tüm gerekçesi
            // "denetlenebilir kalsın"dı. Sessizce gizlersek silmekten
            // farkı kalmaz: yorum kaybolur ve kimin kaldırdığı bilinmez.
            //
            // ⚠️ TargetUserId burada yorumu YAZAN kişi. AuditLog
            // "kim kime ne yaptı" tablosu; burada işlem yorum üzerinden
            // ama etkilenen kişi yorum sahibi.
            var adminId = GetUserId();

            var admin = await _context.Users
                .Where(u => u.Id == adminId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync() ?? "Bilinmiyor";

            var yorumSahibi = await _context.Users
                .Where(u => u.Id == yorum.UserId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync() ?? "Bilinmiyor";

            _context.AuditLogs.Add(new AuditLog
            {
                ActorUserId = adminId,
                ActorName = admin,
                TargetUserId = yorum.UserId,
                TargetName = yorumSahibi,
                Action = gizle ? "yorum_gizlendi" : "yorum_gosterildi",

                // Eski/yeni değer alanlarına yorumun kimliğini yazıyoruz.
                // Denetim ekranında "hangi yorum" sorusunun cevabı
                // olmadan kayıt işe yaramaz.
                OldValue = $"Yorum #{yorum.Id} ({yorum.Rating} yıldız) - gizli: {!gizle}",
                NewValue = $"Yorum #{yorum.Id} ({yorum.Rating} yıldız) - gizli: {gizle}"
            });

            // Tek SaveChanges: yorum güncellemesi ve denetim kaydı
            // aynı işlemde yazılır. Ayrı ayrı kaydetseydik ikincisi
            // başarısız olduğunda kayıtsız bir değişiklik kalırdı.
            await _context.SaveChangesAsync();

            return Ok(new
            {
                mesaj = gizle
                    ? "Yorum gizlendi. Müşterilere gösterilmeyecek ve puan ortalamasına girmeyecek."
                    : "Yorum tekrar görünür yapıldı.",
                gizli = yorum.IsHidden
            });
        }


    }
}