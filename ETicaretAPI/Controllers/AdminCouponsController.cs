using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;

namespace ETicaretAPI.Controllers
{
    // KUPON YÖNETİMİ — SADECE ADMİN
    //
    // Neden CouponsController'dan ayrı bir dosya?
    //   CouponsController müşteri tarafıdır: "bu kodu deneyeyim" der.
    //   Burası mağaza tarafıdır: kupon oluşturur, düzenler, kapatır.
    //   Farklı aktör, farklı yetki, farklı iş kuralları → farklı dosya.
    //
    //
    // Neden Roles = "admin" yetiyor, "superadmin" yazmıyoruz?
    //   TokenService süperadmin'e HEM "superadmin" HEM "admin" claim'i
    //   veriyor. Yani hiyerarşi token üretiminde bir kez kurulmuş durumda.
    //   Her controller'da rol listesi saymak, hiyerarşiyi 20 yere kopyalamak
    //   olurdu — yeni rol eklendiğinde hepsini gezmek gerekirdi.
    [Route("api/admin/coupons")]
    [ApiController]
    [Authorize(Roles = "admin")]
    public class AdminCouponsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AdminCouponsController(AppDbContext context)
        {
            _context = context;
        }

        // Kuponu kimin oluşturduğunu kaydetmek için.
        // İstekten DEĞİL, token'dan okunuyor — client kendini
        // başkası gibi gösteremesin.
        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // ================================================================
        // 🔴 GET /api/admin/coupons
        //     ?search=&durum=&page=1&pageSize=10
        // ================================================================
        [HttpGet]
        public async Task<IActionResult> GetCoupons(
            [FromQuery] string? search,
            [FromQuery] string? durum,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            // Client saçma sayfa numarası gönderirse düzelt.
            // pageSize'a tavan koymak önemli: biri ?pageSize=999999 derse
            // tüm tabloyu belleğe çekeriz, sunucu boğulur.
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 10;
            }

            var query = _context.Coupons.AsQueryable();

            // ---------- ARAMA ----------
            if (!string.IsNullOrWhiteSpace(search))
            {
                var arama = search.Trim();

                query = query.Where(c =>
                    c.Code.Contains(arama) ||
                    c.Description.Contains(arama));
            }

            // ---------- DURUM FİLTRESİ ----------
            //
            // ⚠️ BURASI ÖNEMLİ BİR NOKTA
            //
            // Durum türetilmiş bir değer — veritabanında "Durum" kolonu YOK.
            // Aşağıdaki DurumHesapla() metodu bunu C# tarafında hesaplıyor.
            //
            // Peki neden burada aynı mantığı bir daha yazıyoruz?
            //   Çünkü EF Core bir C# metodunu SQL'e ÇEVİREMEZ.
            //   query.Where(c => DurumHesapla(c) == "aktif") yazsaydık
            //   EF "bunu çeviremem" der ve tüm tabloyu belleğe çekip
            //   orada filtrelerdi (client-side evaluation) → 10.000 kupon
            //   varsa 10.000'ini de RAM'e alırdık.
            //
            //   O yüzden FİLTRELEME SQL'de (aşağıdaki Where'ler),
            //   GÖSTERİM C#'ta (DurumHesapla) yapılıyor.
            //
            // ⚠️ İki yerdeki mantık AYNI kalmalı. Birini değiştirirsen
            //    diğerini de değiştir — yoksa "Aktif" filtresi pasif kupon
            //    gösterir. (Bu bilinçli bir taviz; alternatifi performans
            //    felaketi.)
            var simdi = DateTime.UtcNow;

            query = durum switch
            {
                "aktif" => query.Where(c =>
                    c.IsActive &&
                    c.StartsAt <= simdi &&
                    c.EndsAt >= simdi &&
                    (!c.UsageLimit.HasValue || c.UsedCount < c.UsageLimit.Value)),

                "pasif" => query.Where(c => !c.IsActive),

                "baslamadi" => query.Where(c =>
                    c.IsActive &&
                    c.StartsAt > simdi),

                "suresi_dolmus" => query.Where(c =>
                    c.IsActive &&
                    c.EndsAt < simdi),

                "tukendi" => query.Where(c =>
                    c.IsActive &&
                    c.UsageLimit.HasValue &&
                    c.UsedCount >= c.UsageLimit.Value),

                // Tanımadığımız filtre gelirse hiç filtreleme.
                // Ham metni sorguya sokmuyoruz — whitelist mantığı.
                _ => query
            };

            var toplam = await query.CountAsync();

            // Kategori adını da göstermek istiyoruz ama her kupon için
            // ayrı sorgu atmak N+1 problemi olurdu. Alt sorgu ile
            // tek SQL'de hallediyoruz.
            var kayitlar = await query
                .OrderByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    c.Id,
                    c.Code,
                    c.Description,
                    c.DiscountType,
                    c.DiscountValue,
                    c.MinOrderAmount,
                    c.MaxDiscountAmount,
                    c.StartsAt,
                    c.EndsAt,
                    c.UsageLimit,
                    c.UsageLimitPerUser,
                    c.UsedCount,
                    c.IsActive,
                    c.CategoryId,
                    c.CreatedAt,

                    kategoriAdi = _context.Categories
                        .Where(k => k.Id == c.CategoryId)
                        .Select(k => k.Name)
                        .FirstOrDefault()
                })
                .ToListAsync();

            // Durum etiketini BURADA ekliyoruz — veritabanından döndükten
            // sonra, bellekte. Sadece pageSize kadar kayıt var (en fazla
            // 100), yani maliyeti sıfıra yakın.
            var kuponlar = kayitlar.Select(c => new
            {
                c.Id,
                c.Code,
                c.Description,
                c.DiscountType,
                c.DiscountValue,
                c.MinOrderAmount,
                c.MaxDiscountAmount,
                c.StartsAt,
                c.EndsAt,
                c.UsageLimit,
                c.UsageLimitPerUser,
                c.UsedCount,
                c.IsActive,
                c.CategoryId,
                c.kategoriAdi,
                c.CreatedAt,

                durum = DurumHesapla(
                    c.IsActive, c.StartsAt, c.EndsAt, c.UsageLimit, c.UsedCount)
            });

            var toplamSayfa = (int)Math.Ceiling(toplam / (double)pageSize);

            return Ok(new
            {
                kuponlar = kuponlar,
                toplam = toplam,
                sayfa = page,
                sayfaBoyutu = pageSize,
                toplamSayfa = toplamSayfa
            });
        }

        // ================================================================
        // 🔴 GET /api/admin/coupons/5 — tek kupon + kullanım özeti
        // ================================================================
        [HttpGet("{id}")]
        public async Task<IActionResult> GetCoupon(int id)
        {
            var kupon = await _context.Coupons.FindAsync(id);

            if (kupon == null)
            {
                return NotFound(new { mesaj = "Kupon bulunamadı!" });
            }

            var kategoriAdi = kupon.CategoryId.HasValue
                ? await _context.Categories
                    .Where(k => k.Id == kupon.CategoryId.Value)
                    .Select(k => k.Name)
                    .FirstOrDefaultAsync()
                : null;

            // ---------- KULLANIM ÖZETİ ----------
            var kullanimlar = _context.CouponUsages.Where(cu => cu.CouponId == id);

            var kullanimSayisi = await kullanimlar.CountAsync();

            // Kaç FARKLI kişi kullandı? UsedCount toplam kullanımı verir,
            // bu ise erişilen kişi sayısını. Kişi başı limit 3 ise ikisi
            // farklı çıkar.
            var farkliKullanici = await kullanimlar
                .Select(cu => cu.UserId)
                .Distinct()
                .CountAsync();

            // Sum() boş kümede patlar; (decimal?) cast'i ile null
            // döndürüp ?? 0 ile yakalıyoruz. Bu EF'te standart kalıp.
            var toplamIndirim = await kullanimlar
                .SumAsync(cu => (decimal?)cu.DiscountAmount) ?? 0;

            // Bu kuponun getirdiği ciro — kuponla verilen siparişlerin
            // toplamı. "Kupon bize kaça mal oldu, ne kazandırdı" sorusu.
            var getirilenCiro = await kullanimlar
                .Join(_context.Orders,
                      cu => cu.OrderId,
                      o => o.Id,
                      (cu, o) => o.Total)
                .SumAsync(t => (decimal?)t) ?? 0;

            return Ok(new
            {
                kupon.Id,
                kupon.Code,
                kupon.Description,
                kupon.DiscountType,
                kupon.DiscountValue,
                kupon.MinOrderAmount,
                kupon.MaxDiscountAmount,
                kupon.StartsAt,
                kupon.EndsAt,
                kupon.UsageLimit,
                kupon.UsageLimitPerUser,
                kupon.UsedCount,
                kupon.IsActive,
                kupon.CategoryId,
                kategoriAdi = kategoriAdi,
                kupon.CreatedAt,

                durum = DurumHesapla(
                    kupon.IsActive, kupon.StartsAt, kupon.EndsAt,
                    kupon.UsageLimit, kupon.UsedCount),

                // Silinebilir mi? Ön yüz "Sil" butonunu buna bakarak
                // gösterecek. Ama asıl kilit DELETE endpoint'inde —
                // ön yüz sadece butonu gizler, kapıyı backend kilitler.
                silinebilir = kullanimSayisi == 0 && kupon.UsedCount == 0,

                ozet = new
                {
                    kullanimSayisi = kullanimSayisi,
                    farkliKullanici = farkliKullanici,
                    toplamIndirim = toplamIndirim,
                    getirilenCiro = getirilenCiro
                }
            });
        }

        // ================================================================
        // 🔴 POST /api/admin/coupons — yeni kupon
        // ================================================================
        [HttpPost]
        public async Task<IActionResult> CreateCoupon([FromBody] CouponCreateDto dto)
        {
            // Attribute'ların (Required, Range, StringLength) kontrolü.
            // Bunlar "şekil" doğrulaması — alan dolu mu, aralıkta mı.
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Kodu normalize et. KuponServisi.DogrulaAsync da aynı
            // normalizasyonu yapıyor — ikisi aynı olmak ZORUNDA.
            // Farklı olsalardı "indirim10" diye kaydedilen kupon
            // "INDIRIM10" aramasında bulunamazdı.
            var temizKod = dto.Code.Trim().ToUpperInvariant();

            // "İş kuralı" doğrulaması — şekil değil, anlam kontrolü.
            var hata = await KurallariDogrulaAsync(
                dto.DiscountType,
                dto.DiscountValue,
                dto.StartsAt,
                dto.EndsAt,
                dto.CategoryId);

            if (hata != null)
            {
                return BadRequest(new { mesaj = hata });
            }

            // Aynı kod var mı? Veritabanında unique index ZATEN var,
            // yani bu kontrol olmasa da kayıt engellenirdi.
            // Ama SQL'in fırlatacağı hata "duplicate key row in object
            // dbo.Coupons with unique index IX_Coupons_Code" gibi bir
            // şey olurdu — admin bunu okuyup anlayamaz.
            // Önden kontrol edip insan diliyle söylüyoruz.
            var kodVarMi = await _context.Coupons.AnyAsync(c => c.Code == temizKod);

            if (kodVarMi)
            {
                return BadRequest(new
                {
                    mesaj = $"'{temizKod}' kodu zaten kullanılıyor. Başka bir kod seç."
                });
            }

            var kupon = new Coupon
            {
                Code = temizKod,
                Description = dto.Description.Trim(),
                DiscountType = dto.DiscountType,
                DiscountValue = dto.DiscountValue,
                MinOrderAmount = dto.MinOrderAmount,

                // Tavan sadece yüzdeli kuponda anlamlı.
                // "50 TL indirim ama en fazla 30 TL" cümlesi saçma.
                // Client yanlışlıkla göndermiş olabilir; sessizce
                // temizliyoruz ki veritabanında çöp kalmasın.
                MaxDiscountAmount = dto.DiscountType == "yuzde"
                    ? dto.MaxDiscountAmount
                    : null,

                StartsAt = UtcYap(dto.StartsAt),
                EndsAt = UtcYap(dto.EndsAt),
                UsageLimit = dto.UsageLimit,
                UsageLimitPerUser = dto.UsageLimitPerUser,
                CategoryId = dto.CategoryId,
                IsActive = dto.IsActive,

                // Bu üçü DTO'dan GELMİYOR — sunucu dolduruyor.
                UsedCount = 0,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = GetUserId()
            };

            _context.Coupons.Add(kupon);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Yarış koşulu: iki admin aynı anda aynı kodu kaydederse
                // yukarıdaki AnyAsync kontrolü ikisinde de "yok" der,
                // ikisi de kaydetmeye çalışır, unique index ikincisini
                // reddeder. Gerçek koruma İNDEKS'tir, kontrol sadece
                // güzel mesaj içindir.
                return BadRequest(new
                {
                    mesaj = $"'{temizKod}' kodu az önce başkası tarafından oluşturuldu."
                });
            }

            return Ok(new
            {
                mesaj = "Kupon oluşturuldu biladerim!",
                id = kupon.Id,
                kod = kupon.Code
            });
        }

        // ================================================================
        // 🔴 PUT /api/admin/coupons/5 — düzenle
        // ================================================================
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCoupon(
            int id,
            [FromBody] CouponUpdateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var kupon = await _context.Coupons.FindAsync(id);

            if (kupon == null)
            {
                return NotFound(new { mesaj = "Güncellenecek kupon bulunamadı!" });
            }

            var hata = await KurallariDogrulaAsync(
                dto.DiscountType,
                dto.DiscountValue,
                dto.StartsAt,
                dto.EndsAt,
                dto.CategoryId);

            if (hata != null)
            {
                return BadRequest(new { mesaj = hata });
            }

            // Kullanılmış kuponun kurallarını değiştirmek geçmişi
            // bozmaz — CouponUsage.DiscountAmount ve Order.DiscountAmount
            // zaten DONDURULMUŞ durumda. Yani "%10'du, %20 yaptım"
            // dediğinde eski siparişlerdeki indirim aynı kalır.
            // Bu, dondurma (snapshot) deseninin bize kazandırdığı özgürlük.

            kupon.Description = dto.Description.Trim();
            kupon.DiscountType = dto.DiscountType;
            kupon.DiscountValue = dto.DiscountValue;
            kupon.MinOrderAmount = dto.MinOrderAmount;

            kupon.MaxDiscountAmount = dto.DiscountType == "yuzde"
                ? dto.MaxDiscountAmount
                : null;

            kupon.StartsAt = UtcYap(dto.StartsAt);
            kupon.EndsAt = UtcYap(dto.EndsAt);
            kupon.UsageLimit = dto.UsageLimit;
            kupon.UsageLimitPerUser = dto.UsageLimitPerUser;
            kupon.CategoryId = dto.CategoryId;
            kupon.IsActive = dto.IsActive;

            // ⚠️ Code, UsedCount, CreatedAt, CreatedByUserId'e DOKUNMUYORUZ.
            // DTO'da zaten yoklar, ama olsalar bile yazmazdık.

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Kupon güncellendi biladerim!" });
        }

        // ================================================================
        // 🔴 PUT /api/admin/coupons/5/durum — aktif / pasif
        // ================================================================
        //
        // Neden ayrı endpoint? Listeden tek tıkla kapatmak isteyeceğiz.
        // Bunun için tüm kupon formunu göndermek (PUT /coupons/5) hem
        // gereksiz veri taşır hem de aradaki bir alanı yanlışlıkla
        // ezme riski taşır. Tek alanlık işlem = tek alanlık endpoint.
        //
        // StatusToggleDto zaten projede var (ürünlerde kullanılıyor),
        // yeni DTO yazmıyoruz.
        [HttpPut("{id}/durum")]
        public async Task<IActionResult> ToggleDurum(
            int id,
            [FromBody] StatusToggleDto dto)
        {
            var kupon = await _context.Coupons.FindAsync(id);

            if (kupon == null)
            {
                return NotFound(new { mesaj = "Kupon bulunamadı!" });
            }

            kupon.IsActive = dto.IsActive;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                mesaj = dto.IsActive
                    ? "Kupon aktifleştirildi."
                    : "Kupon pasifleştirildi.",
                isActive = kupon.IsActive
            });
        }

        // ================================================================
        // 🔴 DELETE /api/admin/coupons/5 — SADECE hiç kullanılmamışsa
        // ================================================================
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCoupon(int id)
        {
            var kupon = await _context.Coupons.FindAsync(id);

            if (kupon == null)
            {
                return NotFound(new { mesaj = "Silinecek kupon zaten yok!" });
            }

            var kullanimSayisi = await _context.CouponUsages
                .CountAsync(cu => cu.CouponId == id);

            // İKİ kaynağa birden bakıyoruz:
            //   CouponUsages → gerçek kullanım kayıtları
            //   UsedCount    → sayaç
            // Normalde ikisi eşittir. Eşit değilse bir yerde tutarsızlık
            // var demektir ve bu durumda SİLMEMEK doğru taraftır.
            if (kullanimSayisi > 0 || kupon.UsedCount > 0)
            {
                return BadRequest(new
                {
                    mesaj = $"Bu kupon {kullanimSayisi} kez kullanılmış, silinemez. " +
                            "Kullanımdan kaldırmak için pasifleştir."
                });
            }

            _context.Coupons.Remove(kupon);
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Kupon silindi biladerim!" });
        }

        // ================================================================
        // 🔴 GET /api/admin/coupons/5/kullanimlar — kim, ne zaman, ne kadar
        // ================================================================
        [HttpGet("{id}/kullanimlar")]
        public async Task<IActionResult> GetKullanimlar(
            int id,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            var kuponVarMi = await _context.Coupons.AnyAsync(c => c.Id == id);

            if (!kuponVarMi)
            {
                return NotFound(new { mesaj = "Kupon bulunamadı!" });
            }

            var query = _context.CouponUsages.Where(cu => cu.CouponId == id);

            var toplam = await query.CountAsync();

            // Kullanıcı adını ve sipariş numarasını alt sorguyla çekiyoruz.
            // Include kullanmıyoruz çünkü CouponUsage'da navigation property
            // tanımlı değil — bilinçli bir tercih, entity'leri sade tutuyoruz.
            var kayitlar = await query
                .OrderByDescending(cu => cu.UsedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(cu => new
                {
                    cu.Id,
                    cu.UserId,
                    cu.OrderId,
                    cu.DiscountAmount,
                    cu.UsedAt,

                    kullaniciAdi = _context.Users
                        .Where(u => u.Id == cu.UserId)
                        .Select(u => u.FullName)
                        .FirstOrDefault(),

                    kullaniciEmail = _context.Users
                        .Where(u => u.Id == cu.UserId)
                        .Select(u => u.Email)
                        .FirstOrDefault(),

                    // Siparişi Id ile değil NUMARA ile gösteriyoruz.
                    // Id teknik anahtar, OrderNumber iş anahtarı —
                    // Aşama 2'de koyduğumuz kural.
                    siparisNo = _context.Orders
                        .Where(o => o.Id == cu.OrderId)
                        .Select(o => o.OrderNumber)
                        .FirstOrDefault(),

                    siparisTutari = _context.Orders
                        .Where(o => o.Id == cu.OrderId)
                        .Select(o => (decimal?)o.Total)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var toplamSayfa = (int)Math.Ceiling(toplam / (double)pageSize);

            return Ok(new
            {
                kullanimlar = kayitlar,
                toplam = toplam,
                sayfa = page,
                sayfaBoyutu = pageSize,
                toplamSayfa = toplamSayfa
            });
        }

        // ================================================================
        // YARDIMCI METOTLAR
        // ================================================================

        // İŞ KURALLARI TEK YERDE
        //
        // Hem POST hem PUT bu metodu çağırıyor. İki yere kopyalasaydık,
        // yarın "yüzde en fazla 90 olsun" dendiğinde birini güncelleyip
        // diğerini unutur, "oluştururken olmuyor ama düzenlerken oluyor"
        // gibi bir hataya düşerdik.
        //
        // Hata varsa mesajı, yoksa null döner.
        // Neden bool + out string değil? null-döndürme kalıbı daha kısa
        // ve C#'ta "?" ile nullable olduğu derleyici tarafından takip
        // edilebiliyor.
        private async Task<string?> KurallariDogrulaAsync(
            string discountType,
            decimal discountValue,
            DateTime startsAt,
            DateTime endsAt,
            int? categoryId)
        {
            // ---------- 1) İNDİRİM TİPİ WHITELIST ----------
            // Kabul edilenler dışındaki her şey reddedilir.
            // "Yasaklıları say" değil "izin verilenleri say" mantığı —
            // yeni bir saldırı biçimi düşünmek zorunda kalmıyoruz.
            if (discountType != "yuzde" && discountType != "tutar")
            {
                return "İndirim tipi 'yuzde' veya 'tutar' olmalı.";
            }

            // ---------- 2) YÜZDE 0-100 ARASI ----------
            // %150 indirim = müşteriye para vermek demek.
            // KuponServisi'nde "indirim sepetten büyük olamaz" koruması
            // var ama hatalı veriyi en baştan almamak daha doğru.
            if (discountType == "yuzde" && (discountValue <= 0 || discountValue > 100))
            {
                return "Yüzde indirim 0 ile 100 arasında olmalı.";
            }

            // ---------- 3) TARİH SIRASI ----------
            if (endsAt <= startsAt)
            {
                return "Bitiş tarihi başlangıç tarihinden sonra olmalı.";
            }

            // ---------- 4) KATEGORİ GERÇEKTEN VAR MI ----------
            // Olmayan kategoriye bağlı kupon hiçbir zaman çalışmaz ama
            // hata da vermez — sessizce "bu kupon sepetindeki ürünlerde
            // geçerli değil" der ve admin saatlerce sebebini arar.
            if (categoryId.HasValue)
            {
                var kategoriVarMi = await _context.Categories
                    .AnyAsync(k => k.Id == categoryId.Value);

                if (!kategoriVarMi)
                {
                    return "Seçilen kategori bulunamadı.";
                }
            }

            return null; // her şey yolunda
        }

        // DURUM TÜRETME
        //
        // Veritabanında "Durum" diye bir kolon YOK ve olmamalı.
        // Sebebi: bir kuponun süresi gece 23:59'da dolar. Kolonda
        // tutsaydık o kolonu güncelleyecek bir zamanlanmış iş yazmak
        // gerekirdi ve iş gecikirse DB "aktif" derken KuponServisi
        // "süresi dolmuş" derdi — iki gerçek çatışırdı.
        //
        // SIRA ÖNEMLİ: yukarıdan aşağı ilk uyan kazanır.
        // Pasif en üstte çünkü admin elle kapattıysa diğer hiçbir şey
        // önemli değil.
        private static string DurumHesapla(
            bool isActive,
            DateTime startsAt,
            DateTime endsAt,
            int? usageLimit,
            int usedCount)
        {
            if (!isActive)
            {
                return "pasif";
            }

            var simdi = DateTime.UtcNow;

            if (simdi > endsAt)
            {
                return "suresi_dolmus";
            }

            if (simdi < startsAt)
            {
                return "baslamadi";
            }

            if (usageLimit.HasValue && usedCount >= usageLimit.Value)
            {
                return "tukendi";
            }

            return "aktif";
        }

        // TARİHİ UTC'YE ÇEVİR
        //
        // Neden gerek var?
        //   KuponServisi tarihleri DateTime.UtcNow ile karşılaştırıyor.
        //   Yani veritabanındaki tarihler UTC olmak ZORUNDA.
        //
        //   Ama JSON'dan gelen tarih üç farklı halde olabilir:
        //     "2026-08-01T00:00:00Z"      → Kind = Utc
        //     "2026-08-01T00:00:00+03:00" → Kind = Local
        //     "2026-08-01T00:00:00"       → Kind = Unspecified
        //
        //   Üçünü de aynı kefeye koymazsak, Türkiye'den (UTC+3) girilen
        //   bir tarih 3 saat kaymış olarak kaydedilir ve kupon
        //   beklenenden 3 saat erken/geç başlar.
        //
        //   Unspecified geldiğinde "bu zaten UTC'dir" varsayıyoruz —
        //   çünkü admin panelini biz yazıyoruz ve UTC göndereceğiz.
        private static DateTime UtcYap(DateTime tarih)
        {
            return tarih.Kind switch
            {
                DateTimeKind.Utc => tarih,
                DateTimeKind.Local => tarih.ToUniversalTime(),
                _ => DateTime.SpecifyKind(tarih, DateTimeKind.Utc)
            };
        }
    }
}