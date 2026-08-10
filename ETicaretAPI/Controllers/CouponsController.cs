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

        // ⭐ YENİ — kupon uygulanınca kargo durumu da değişebilir.
        //
        // Neden gerekli? Sepet 550 TL, eşik 500 TL → kargo bedava.
        // Müşteri 100 TL'lik kupon uygularsa indirimli tutar 450'ye
        // düşer ve kargo ÜCRETLİ hale gelir.
        //
        // Bu bilgiyi burada döndürmezsek mobil "indirim uygulandı,
        // yeni toplam 450" der; müşteri onay ekranına geçince 499,90
        // görür. Sürpriz fiyat artışı, sepet terk etmenin bir
        // numaralı sebebidir.
        private readonly SepetHesaplayici _hesaplayici;

        public CouponsController(
            AppDbContext context,
            KuponServisi kuponServisi,
            SepetHesaplayici hesaplayici)               // ⭐ YENİ
        {
            _context = context;
            _kuponServisi = kuponServisi;
            _hesaplayici = hesaplayici;                 // ⭐ YENİ
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // ⭐ YENİ — 🟡 GET /api/coupons/{kod}
        //
        // Kampanya detay ekranı için: kuponun KOŞULLARINI gösterir,
        // sepetle ilgilenmez.
        //
        // ⚠️ NEDEN "dogrula" YETMİYOR?
        // O uç sepete bakıyor ve "şu an ne kadar iner" diyor.
        // Kampanya ekranında sepet yok; sorulan soru "bu kupon ne
        // vaat ediyor". Sepetsiz çağırmak için dogrula'yı esnetmek,
        // tek metodu iki farklı işe koşmak olurdu.
        //
        // ⚠️ NE DÖNMÜYOR — bilerek:
        //   • UsedCount / UsageLimit → kaç kişi kullandı bilgisi
        //     mağazanın işi; müşteriye "3 kişi kaldı" demek bir
        //     aciliyet oyunu ve elimizde onu doğru gösterecek
        //     canlı bir sayaç yok
        //   • CreatedByUserId → tamamen iç bilgi
        //
        // ⚠️ Kupon YOKSA 404. Kampanya ekranı o kartı hiç çizmiyor;
        // "kupon bulunamadı" diye boş bir kutu göstermek müşteriye
        // yapabileceği bir şey sunmazdı.
        //
        // ⚠️ Kod normalleştirilerek aranıyor — müşteri afişteki
        // Türkçe karakterli hâlini yazsa da bulunsun.
        [HttpGet("{kod}")]
        public async Task<IActionResult> KuponuGetir(string kod)
        {
            var temizKod = KuponServisi.KoduNormallestir(kod);

            var kupon = await _context.Coupons
                .Where(c => c.Code == temizKod && c.IsActive)
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
                    c.CategoryId,
                    c.IndirimliUrunlerdeGecerli
                })
                .FirstOrDefaultAsync();

            if (kupon == null)
            {
                return NotFound(new { mesaj = "Kupon bulunamadı." });
            }

            return Ok(kupon);
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

            // ⭐ YENİ — kargo dahil tam özet.
            //
            // Toplamı burada "araToplam - indirim" diye
            // hesaplamıyoruz artık; sipariş oluştururken kullanılan
            // AYNI servisi çağırıyoruz. Böylece önizlemede gösterilen
            // rakam ile gerçekte tahsil edilen rakam ayrışamaz.
            var ozet = _hesaplayici.Hesapla(araToplam, sonuc.IndirimTutari);

            return Ok(new
            {
                mesaj = sonuc.Mesaj,
                kod = sonuc.Kupon!.Code,
                aciklama = sonuc.Kupon.Description,

                araToplam = ozet.AraToplam,
                indirim = ozet.Indirim,

                // ⭐ YENİ — kargo bilgileri
                kargoUcreti = ozet.KargoUcreti,
                ucretsizKargoKazanildi = ozet.UcretsizKargoKazanildi,
                ucretsizKargoyaKalan = ozet.UcretsizKargoyaKalan,

                // ⚠️ ALAN ADI DEĞİŞMEDİ ama ANLAMI DEĞİŞTİ:
                // artık kargo dahil nihai tutar.
                //
                // Adı "yeniToplam" bırakmak bilinçli — mobil bu alanı
                // zaten okuyor ve değiştirsek eski sürüm uygulamalar
                // undefined görürdü. Alan ADI sözleşmedir, kolayca
                // değiştirilmez. (Aşama 11'de mobil güncellenince
                // adı netleştirilebilir.)
                yeniToplam = ozet.Toplam
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
                          BirimFiyat = p.Price,

                          // ⭐ YENİ (B1) — kupon "indirimli üründe
                          // geçmez" ise bu kalem matrahtan düşecek.
                          //
                          // ⚠️ Koşul OrdersController'daki ile BİREBİR
                          // aynı olmak zorunda: sepette önizlenen
                          // indirim ile siparişte uygulanan indirim
                          // farklı çıkarsa müşteri gördüğü tutarı
                          // ödemez.
                          IndirimliMi = p.EskiFiyat != null && p.EskiFiyat > p.Price
                      })
                .ToListAsync();
        }
    }
}