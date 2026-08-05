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
                          BirimFiyat = p.Price
                      })
                .ToListAsync();
        }
    }
}