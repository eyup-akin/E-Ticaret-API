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

        public CartController(AppDbContext context)
        {
            _context = context;
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
                          ProductImageUrl = _context.ProductImages
                              .Where(pi => pi.ProductId == p.Id)
                              .OrderByDescending(pi => pi.IsMain)
                              .ThenBy(pi => pi.SortOrder)
                              .Select(pi => pi.Url)
                              .FirstOrDefault()
                      })
                .ToListAsync();

            return Ok(cart);
        }

        // Sepette bir ürünün en fazla kaç adet olabileceği.
        //
        // Neden sabit? Üç yerde aynı sayı geçiyor: CartAddDto'daki Range,
        // mobildeki adet seçici ve aşağıdaki kırpma. Sihirli sayıyı koda
        // gömmek yerine isim vermek, değiştirmek gerektiğinde tek yer arattırır.
        private const int SepetMaksAdet = 99;

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

            // Ürün gerçekten var mı?
            //
            // Bu kontrol yarış koşuluna açık (ürün tam bu anda silinebilir) ama
            // sorun değil: silinirse aşağıdaki INSERT foreign key hatası verir
            // ve global hata middleware'i yakalar. Buradaki kontrol güzel mesaj
            // içindir, koruma değil.
            var urunVarMi = await _context.Products.AnyAsync(p => p.Id == dto.ProductId);
            if (!urunVarMi)
            {
                return NotFound(new { mesaj = "Böyle bir ürün yok biladerim!" });
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
                         && c.Quantity > SepetMaksAdet)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    c => c.Quantity,
                    c => SepetMaksAdet));

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