using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;

namespace ETicaretAPI.Services
{
    // ⭐ YENİ — SEPETE EKLEME KURALI, TEK YERDE
    //
    // Kural CartController.AddToCart'ın gövdesindeydi. "Siparişi
    // tekrarla" ikinci tüketici oldu; ikinci tüketici çıktığı an
    // ortak yere taşınır.
    //
    // ⚠️ Kopyalasaydık ayrışacak yer belliydi: upsert'in yarış
    // koşulu korumaları ve üst sınır kırpması. Biri unutulursa
    // hata sessiz olurdu — sepette mükerrer satır ya da 99'u aşan adet.
    public enum SepeteEklemeSonucu
    {
        Eklendi,

        // Ürün silinmiş ya da satışta değil. İkisi tek sonuç:
        // müşteri açısından ikisi de "alamıyorum".
        UrunYok
    }

    public class SepetEkleyici
    {
        private readonly AppDbContext _context;
        private readonly MagazaAyarlari _ayarlar;

        public SepetEkleyici(AppDbContext context, MagazaAyarlari ayarlar)
        {
            _context = context;
            _ayarlar = ayarlar;
        }

        /// <summary>
        /// Ürünü kullanıcının sepetine ekler; satır varsa adedi artırır.
        /// </summary>
        /// <remarks>
        /// ⚠️ StokDefteri'nin aksine SaveChanges ÇAĞIRIR. Sebebi
        /// yarış koşulu koruması: adet artışı ExecuteUpdate ile
        /// veritabanında yapılıyor, yani ertelenemez. Çağıranın
        /// transaction'ına yazılması gereken bir şey de yok.
        /// </remarks>
        public async Task<SepeteEklemeSonucu> EkleAsync(int userId, int productId, int adet)
        {
            // Ürün gerçekten var mı VE satışta mı?
            //
            // ⚠️ Bu kontrol yarış koşuluna açık — ürün tam bu anda pasife
            // alınabilir. Ve bu SORUN DEĞİL: pasif ürünün sepete girmesi
            // kimseye zarar vermez, asıl kilit sipariş anındaki atomik
            // UPDATE'te. Buradaki kontrol erken ve anlaşılır mesaj için.
            //
            // ⚠️ Fiyat da aynı sorguda çekiliyor (5.4): müşterinin şu an
            // gördüğü fiyat EklenmeFiyati'ne yazılıyor. "Önce var mı bak,
            // sonra fiyatı çek" deseydik veritabanına iki tur giderdik.
            var urun = await _context.Products
                .Where(p => p.Id == productId && p.IsActive)
                .Select(p => new { p.Price })
                .FirstOrDefaultAsync();

            if (urun == null)
            {
                return SepeteEklemeSonucu.UrunYok;
            }

            var guncelFiyat = urun.Price;

            // ---------- 1) SATIR VARSA ATOMİK ARTIR ----------
            //
            // Okuma yok — veritabanı mevcut değeri kendi okuyup üstüne
            // ekliyor, hepsi satır kilidi altında tek cümlede. "Oku-
            // kontrol et-yaz" olsaydı iki hızlı istek aynı değeri okur
            // ve biri diğerinin artışını silerdi.
            //
            // ⚠️ EklenmeFiyati HER EKLEMEDE TAZELENİYOR: müşteri ürünü
            // tekrar sepete atıyorsa güncel fiyatı görüp basmış demektir.
            // Eskisini korusaydık kendi kabul ettiği fiyat için ona
            // "dikkat, fiyat değişti" uyarısı gösterirdik.
            var etkilenen = await _context.CartItems
                .Where(c => c.UserId == userId && c.ProductId == productId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Quantity, c => c.Quantity + adet)
                    .SetProperty(c => c.EklenmeFiyati, guncelFiyat));

            // ---------- 2) SATIR YOKTUYSA EKLE ----------
            if (etkilenen == 0)
            {
                var yeniOge = new CartItem
                {
                    UserId = userId,
                    ProductId = productId,
                    Quantity = adet,
                    EklenmeFiyati = guncelFiyat
                };

                _context.CartItems.Add(yeniOge);

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Tam bu anda başka bir istek satırı oluşturdu ve
                    // benzersiz indeks bizi reddetti. Hata değil, beklenen
                    // yarış sonucu — yapılacak şey artırmaya geçmek.
                    //
                    // ⚠️ Başarısız Add hâlâ "Added" durumda; detach
                    // etmezsek bir sonraki SaveChanges aynı INSERT'i
                    // tekrar denemeye kalkar.
                    _context.Entry(yeniOge).State = EntityState.Detached;

                    await _context.CartItems
                        .Where(c => c.UserId == userId && c.ProductId == productId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(c => c.Quantity, c => c.Quantity + adet)
                            .SetProperty(c => c.EklenmeFiyati, guncelFiyat));
                }
            }

            // ---------- 3) ÜST SINIRA KIRP ----------
            //
            // Artırma sınırsız olduğu için müşteri butona 50 kez basarsa
            // adet 99'u aşabilir. Ayrı ve koşullu bir UPDATE ile kırpıyoruz.
            //
            // Neden 1. adımdaki koşula "Quantity + adet <= 99" eklemedik?
            // Eklersek etkilenen == 0 iki farklı anlama gelirdi ("satır
            // yok" ve "sınır aşıldı"); ayırt edemez, yanlışlıkla ikinci
            // bir satır eklemeye çalışırdık.
            await _context.CartItems
                .Where(c => c.UserId == userId
                         && c.ProductId == productId
                         && c.Quantity > _ayarlar.SepetMaksAdet)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    c => c.Quantity,
                    c => _ayarlar.SepetMaksAdet));

            return SepeteEklemeSonucu.Eklendi;
        }
    }
}
