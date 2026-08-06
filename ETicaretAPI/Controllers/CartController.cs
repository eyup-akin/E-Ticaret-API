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

        public CartController(
            AppDbContext context,
            ETicaretAPI.Services.MagazaAyarlari ayarlar,
            ETicaretAPI.Services.SepetHesaplayici hesaplayici)   // ⭐ YENİ
        {
            _context = context;
            _ayarlar = ayarlar;
            _hesaplayici = hesaplayici;                          // ⭐ YENİ
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

            var ozet = _hesaplayici.Hesapla(araToplam, 0);

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

                ozet = new
                {
                    araToplam = ozet.AraToplam,
                    kargoUcreti = ozet.KargoUcreti,
                    toplam = ozet.Toplam,
                    ucretsizKargoyaKalan = ozet.UcretsizKargoyaKalan,
                    ucretsizKargoKazanildi = ozet.UcretsizKargoKazanildi
                }
            });
        }

        // Sepette bir ürünün en fazla kaç adet olabileceği.
        //
        // Neden sabit? Üç yerde aynı sayı geçiyor: CartAddDto'daki Range,
        // mobildeki adet seçici ve aşağıdaki kırpma. Sihirli sayıyı koda
        // gömmek yerine isim vermek, değiştirmek gerektiğinde tek yer arattırır.
        

        // 🟡 POST /api/cart — sepete ekle
        //
        // ⚠️ ESKİ KOD İKİ YARIŞ KOŞULUNA AÇIKTI:
        //
        //   1) KAYIP GÜNCELLEME
        //      FirstOrDefault ile adet okunuyor, sonra += yapılıyordu.
        //      Müşteri butona hızlıca iki kez bassa ikisi de 1 okuyup
        //      ikisi de 2 yazardı → sonuç 2, olması gereken 3.
        //
        //   2) MÜKERRER SATIR
        //      "Sepette var mı" kontrolü ile INSERT arasında boşluk vardı.
        //      İki istek aynı anda gelirse ikisi de "yok" görüp ikisi de
        //      eklerdi → aynı ürün sepette iki satır.
        //
        // YENİ YAKLAŞIM — "UPSERT":
        //   Önce oku-karar ver-yaz yapmıyoruz. Doğrudan yazmayı deniyoruz
        //   ve sonucuna bakıyoruz. Stok ve kupon düzeltmelerindeki mantığın
        //   aynısı.
        [HttpPost]
        public async Task<IActionResult> AddToCart([FromBody] CartAddDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetUserId();

            // Ürün gerçekten var mı VE satışta mı?
            //
            // İki koşul tek sorguda: "önce var mı bak, sonra aktif mi bak"
            // demek veritabanına iki tur gitmek olurdu. Aynı satırdan iki
            // bilgi istiyoruz, tek sorgu yeter.
            //
            // ⚠️ Bu kontrol yarış koşuluna açık — ürün tam bu anda pasife
            // alınabilir. Ve bu SORUN DEĞİL: pasif ürünün sepete girmesi
            // kimseye zarar vermez, çünkü asıl kilit sipariş anında
            // (OrdersController'daki atomik UPDATE) duruyor. Buradaki kontrol
            // kullanıcıya ERKEN ve ANLAŞILIR mesaj vermek için, koruma değil.
            //
            // Mesajı bilerek tek tuttuk: "ürün yok" ile "ürün pasif" müşteri
            // açısından aynı sonuca çıkıyor — alamıyor. İkisini ayırmak
            // müşteriye kullanamayacağı bir bilgi verirdi.
            var urunSatistaMi = await _context.Products
                .AnyAsync(p => p.Id == dto.ProductId && p.IsActive);

            if (!urunSatistaMi)
            {
                return NotFound(new { mesaj = "Bu ürün şu anda satışta değil biladerim!" });
            }

            // Lambda içinde kullanacağımız için yerel değişkene alıyoruz
            var adet = dto.Quantity;

            // ---------- 1) SATIR VARSA ATOMİK ARTIR ----------
            //
            // Ürettiği SQL:
            //     UPDATE CartItems
            //     SET Quantity = Quantity + @adet
            //     WHERE UserId = @userId AND ProductId = @productId
            //
            // Okuma yok — veritabanı mevcut değeri kendi okuyup üstüne ekliyor,
            // hepsi satır kilidi altında tek cümlede. Kayıp güncelleme imkânsız.
            var etkilenen = await _context.CartItems
                .Where(c => c.UserId == userId && c.ProductId == dto.ProductId)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    c => c.Quantity,
                    c => c.Quantity + adet));

            // ---------- 2) SATIR YOKTUYSA EKLE ----------
            if (etkilenen == 0)
            {
                var yeniOge = new CartItem
                {
                    UserId = userId,
                    ProductId = dto.ProductId,
                    Quantity = adet
                };

                _context.CartItems.Add(yeniOge);

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Tam bu anda başka bir istek satırı oluşturdu ve benzersiz
                    // indeks bizi reddetti. Bu bir HATA değil, beklenen bir yarış
                    // sonucu — yapılacak şey artırmaya geçmek.
                    //
                    // ⚠️ Başarısız Add hâlâ change tracker'da "Added" durumda.
                    //    Detach etmezsek bu context'te bir sonraki SaveChanges
                    //    aynı INSERT'i tekrar denemeye kalkar.
                    _context.Entry(yeniOge).State = EntityState.Detached;

                    await _context.CartItems
                        .Where(c => c.UserId == userId && c.ProductId == dto.ProductId)
                        .ExecuteUpdateAsync(s => s.SetProperty(
                            c => c.Quantity,
                            c => c.Quantity + adet));
                }
            }

            // ---------- 3) ÜST SINIRA KIRP ----------
            //
            // Artırma sınırsız olduğu için müşteri butona 50 kez basarsa adet
            // 99'u aşabilir. Ayrı ve koşullu bir UPDATE ile kırpıyoruz.
            //
            // Neden 1. adımdaki koşula "Quantity + adet <= 99" eklemedik?
            //   Eklersek etkilenen == 0 iki farklı anlama gelirdi:
            //   "satır yok" ve "sınır aşıldı". Ayırt edemez, yanlışlıkla
            //   ikinci bir satır eklemeye çalışırdık.
            //
            // Bu cümle idempotent: adet 99'un altındaysa hiçbir satırı
            // etkilemez, üstündeyse 99'a çeker. İki kez çalışsa da zarar yok.
            await _context.CartItems
                .Where(c => c.UserId == userId
                         && c.ProductId == dto.ProductId
                         && c.Quantity > _ayarlar.SepetMaksAdet)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    c => c.Quantity,
                    c => _ayarlar.SepetMaksAdet));

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