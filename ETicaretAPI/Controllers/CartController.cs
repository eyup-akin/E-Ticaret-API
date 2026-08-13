using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;

namespace ETicaretAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // tüm sepet işlemleri giriş ister
    public class CartController : ControllerBase
    {
        private readonly AppDbContext _context;
        // ⭐ YENİ — ayarlardan geliyor, artık koda gömülü değil.
        private readonly ETicaretAPI.Services.MagazaAyarlari _ayarlar;

        // ⭐ YENİ — sepet özetini (kargo dahil) üreten servis.
        //
        // Kargo eşiğini burada elle hesaplamıyoruz. Aynı hesabı
        // /coupons/dogrula ve sipariş oluşturma da yapıyor; üçünün
        // aynı sonucu vermesinin tek garantisi aynı kodu çağırmaları.
        //
        // Alternatif, eşik hesabını mobile bırakmaktı: sunucu sadece
        // ayarları gönderir, mobil "kaç TL kaldı"yı kendi bulurdu.
        // Seçmedik çünkü o formül sipariş anındaki formülden sessizce
        // ayrışabilir ve müşteri sepette gördüğü tutarı ödemez.
        private readonly ETicaretAPI.Services.SepetHesaplayici _hesaplayici;

        // ⭐ YENİ — kombin indirimi: sepette bir kombinin TÜM ürünleri
        // varsa indirim otomatik uygulanıyor.
        private readonly ETicaretAPI.Services.KombinServisi _kombin;

        // ⭐ YENİ — sepete ekleme kuralı (upsert + üst sınır).
        // "Siparişi tekrarla" da aynı kuralı çağırıyor.
        private readonly ETicaretAPI.Services.SepetEkleyici _ekleyici;

        public CartController(
            AppDbContext context,
            ETicaretAPI.Services.MagazaAyarlari ayarlar,
            ETicaretAPI.Services.SepetHesaplayici hesaplayici,   // ⭐ YENİ
            ETicaretAPI.Services.KombinServisi kombin,           // ⭐ YENİ
            ETicaretAPI.Services.SepetEkleyici ekleyici)         // ⭐ YENİ
        {
            _context = context;
            _ayarlar = ayarlar;
            _hesaplayici = hesaplayici;                          // ⭐ YENİ
            _kombin = kombin;
            _ekleyici = ekleyici;                                // ⭐ YENİ
        }

        // Token'dan giriş yapmış kullanıcının id'sini okur
        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // 🟡 GET /api/cart — benim sepetim
        [HttpGet]
        public async Task<IActionResult> GetCart()
        {
            var userId = GetUserId();

            // Sepet öğelerini ürün bilgisiyle birleştir (join)
            var cart = await _context.CartItems
                .Where(c => c.UserId == userId)
                .Join(_context.Products,
                      c => c.ProductId,
                      p => p.Id,
                      (c, p) => new CartItemDto
                      {
                          Id = c.Id,
                          ProductId = p.Id,
                          ProductName = p.Name,
                          ProductPrice = p.Price,
                          Quantity = c.Quantity,
                          IsActive = p.IsActive,          // ⭐ YENİ

                          // ⭐ YENİ (5.4) — sepete atılırken görülen fiyat.
                          //
                          // Karşılaştırmayı BURADA yapmıyoruz: FiyatDegisti
                          // ve FiyatFarki, DTO'da hesaplanan özellikler.
                          // Buraya yazsaydık aynı kural sipariş onay
                          // akışında ikinci kez yazılmak zorunda kalırdı.
                          EklenmeFiyati = c.EklenmeFiyati,
                          ProductImageUrl = _context.ProductImages
                              .Where(pi => pi.ProductId == p.Id)
                              .OrderByDescending(pi => pi.IsMain)
                              .ThenBy(pi => pi.SortOrder)
                              .Select(pi => pi.Url)
                              .FirstOrDefault()
                      })
                .ToListAsync();

            // ⭐ YENİ — SEPET ÖZETİ (kargo dahil)
            //
            // ⚠️ İNDİRİM PARAMETRESİ 0 — bilerek.
            //
            // Sepet ekranında henüz kupon uygulanmamıştır; kupon
            // kutusu sipariş onay adımında. Buraya bir indirim
            // uydurmak, "kargo bedava" rozetinin yanlış tutara göre
            // hesaplanmasına yol açardı.
            //
            // Kupon girildiğinde /coupons/dogrula AYNI servisi
            // indirimle çağırıp güncel özeti döndürüyor — mobil o
            // cevabı kullanacak, kendi hesap yapmayacak.
            var araToplam = cart.Sum(k => k.ProductPrice * k.Quantity);

            // ⭐ YENİ — kombin indirimi (kupondan AYRI).
            // Kupon sipariş onayında giriliyor; kombin indirimi ise
            // sepetin içeriğinden doğuyor, burada görünmeli.
            var kombinIndirimi = await _kombin.SepetIndirimiAsync(
                cart.Select(k => k.ProductId).Distinct().ToList());

            var ozet = _hesaplayici.Hesapla(araToplam, kombinIndirimi);

            // ⭐ DEĞİŞTİ — CEVAP ARTIK DÜZ DİZİ DEĞİL, NESNE.
            //
            // Eskiden doğrudan kalem dizisi dönüyordu. Özeti diziye
            // sığdırmanın bir yolu yok, sarmalamak zorundaydık.
            //
            // Alternatifi özet için ayrı bir uç açmaktı (GET
            // /api/cart/ozet). Seçmedik: sepet ile özetin AYNI ANIN
            // verisi olması gerekiyor. İki ayrı istekte arada adet
            // değişirse ekranda 3 ürün görünürken toplam 2 ürünün
            // olurdu.
            //
            // ⚠️ Kalemlerin İÇİNDEKİ alan adlarına dokunulmadı
            // (id, productId, productName, productPrice, quantity,
            // isActive, productImageUrl) — mobil onları okuyor.
            // Kıran tek şey en dış sarmalayıcı.
            return Ok(new
            {
                kalemler = cart,

                // ⭐ YENİ (5.4) — sepette fiyatı değişen ürün var mı?
                //
                // Kalemlerin içinde zaten satır satır duruyor; bu, mobilin
                // her açılışta listeyi tarayıp kendi bayrağını çıkarmasını
                // önlüyor. Aynı soruyu iki taraf da soruyorsa cevabı
                // sunucu versin — mobil "hangi satırda" diye bakmadan
                // uyarı şeridini çizip çizmeyeceğini biliyor.
                //
                // ⚠️ Bu bir SAYIM değil, VAR/YOK. Kaç ürünün değiştiği
                // satırlardan sayılabilir; burada ikinci bir sayı tutmak
                // listeyle çelişebilecek bir kaynak yaratırdı.
                fiyatDegisenVar = cart.Any(k => k.FiyatDegisti),

                ozet = new
                {
                    araToplam = ozet.AraToplam,

                    // ⚠️ Ayrı satır: müşteri indirimin nereden
                    // geldiğini görmeli.
                    kombinIndirimi = kombinIndirimi,

                    kargoUcreti = ozet.KargoUcreti,
                    toplam = ozet.Toplam,
                    ucretsizKargoyaKalan = ozet.UcretsizKargoyaKalan,
                    ucretsizKargoKazanildi = ozet.UcretsizKargoKazanildi
                }
            });
        }

        // 🟡 POST /api/cart — sepete ekle
        //
        // ⭐ DEĞİŞTİ — upsert mantığı SepetEkleyici servisine taşındı.
        //
        // Kural (yarış koşulu korumaları + 99 kırpması) burada tek
        // tüketicisi olduğu sürece duruyordu; "siparişi tekrarla" ikinci
        // tüketici olunca ortak yere çıktı. Gerekçenin tamamı serviste.
        [HttpPost]
        public async Task<IActionResult> AddToCart([FromBody] CartAddDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetUserId();

            var sonuc = await _ekleyici.EkleAsync(userId, dto.ProductId, dto.Quantity);

            if (sonuc == ETicaretAPI.Services.SepeteEklemeSonucu.UrunYok)
            {
                return NotFound(new { mesaj = "Bu ürün şu anda satışta değil biladerim!" });
            }

            return Ok(new { mesaj = "Ürün sepete eklendi biladerim!" });
        }


        // 🟡 PUT /api/cart/5 — adet güncelle
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateQuantity(int id, [FromBody] CartAddDto dto)
        {
            var userId = GetUserId();

            // Sadece KENDİ sepet öğesini güncelleyebilir
            var item = await _context.CartItems
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

            if (item == null)
            {
                return NotFound(new { mesaj = "Sepet öğesi bulunamadı!" });
            }

            // ⚠️ EklenmeFiyati'na BURADA DOKUNULMUYOR — bilerek. (5.4)
            //
            // Bu uç sepet ekranındaki − / + butonlarının çağırdığı yer.
            // Fiyatı tazeleseydik şu olurdu: müşteri "fiyat 100'den
            // 120'ye çıktı" uyarısını görür, adedi bir azaltır ve uyarı
            // SESSİZCE KAYBOLURDU — hiçbir şey kabul etmemiş olmasına
            // rağmen.
            //
            // Tanık yalnızca müşteri ürünü ürün sayfasından TEKRAR
            // eklerken tazeleniyor; orada güncel fiyatı görüp öyle
            // basıyor. Sepetteki adet oynatma böyle bir görme değil.
            item.Quantity = dto.Quantity;
            await _context.SaveChangesAsync();
            return Ok(new { mesaj = "Adet güncellendi!" });
        }

        // 🟡 DELETE /api/cart/5 — sepetten çıkar
        [HttpDelete("{id}")]
        public async Task<IActionResult> RemoveFromCart(int id)
        {
            var userId = GetUserId();

            var item = await _context.CartItems
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

            if (item == null)
            {
                return NotFound(new { mesaj = "Sepet öğesi bulunamadı!" });
            }

            _context.CartItems.Remove(item);
            await _context.SaveChangesAsync();
            return Ok(new { mesaj = "Ürün sepetten çıkarıldı!" });
        }
    }
}