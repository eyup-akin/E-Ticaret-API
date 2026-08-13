using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;   // ⭐ YENİ — içerik imzası (SHA-256)
using System.Text;                    // ⭐ YENİ — UTF-8 baytları
using ETicaretAPI.Data;
using ETicaretAPI.Models;

namespace ETicaretAPI.Controllers
{
    // ⭐ YENİ — HIZLI SİPARİŞLER
    //
    // Müşterinin "sonra yine alırım" diye kaydettiği siparişler.
    //
    // ⚠️ BU CONTROLLER SİPARİŞ OLUŞTURMUYOR, SEPETE DE EKLEMİYOR.
    // Yaptığı tek şey işaretlemek/işareti kaldırmak. Kaydedilmiş bir
    // siparişi tekrar vermek POST /api/orders/{id}/tekrarla ile
    // oluyor ve o uç zaten var — ikinci bir yol yazmıyoruz.
    //
    [Route("api/hizli-siparisler")]
    [ApiController]
    [Authorize]
    public class HizliSiparislerController : ControllerBase
    {
        private readonly AppDbContext _context;

        public HizliSiparislerController(AppDbContext context)
        {
            _context = context;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // ⭐ YENİ — SİPARİŞİN İÇERİK İMZASI
        //
        // ⚠️ TARİF HizliSiparis.IcerikImzasi'nda yazılı ve migration'daki
        // geri doldurma SQL'i BİREBİR aynısını üretiyor. Buradaki
        // sıralama, ayraç ya da hash değişirse o SQL de değişmeli —
        // yoksa eski satırlar yeni kayıtlarla çakışmaz ve mükerrer
        // içerik sessizce geri gelir.
        //
        // Girdi: (ürün, adet) çiftleri.
        private static string ImzaUret(IEnumerable<(int ProductId, int Adet)> kalemler)
        {
            // ⚠️ GRUPLAMA ŞART: aynı ürün siparişte iki ayrı kalem
            // olarak durabiliyor ("siparişi tekrarla" ucu da bu yüzden
            // grupluyor). Gruplamasaydık {A×1, A×1} ile {A×2} farklı
            // imza üretirdi — oysa ikisi de aynı sepet.
            var metin = string.Join("|", kalemler
                .GroupBy(k => k.ProductId)
                .OrderBy(g => g.Key)
                .Select(g => g.Key + "x" + g.Sum(k => k.Adet)));

            var baytlar = Encoding.UTF8.GetBytes(metin);

            // Küçük harf hex — TokenService.Hashle ile aynı biçim.
            return Convert.ToHexString(SHA256.HashData(baytlar)).ToLowerInvariant();
        }

        // Siparişin kalemlerinden imzayı üretir.
        private async Task<string> SiparisImzasiAsync(int orderId)
        {
            var kalemler = await _context.OrderItems
                .Where(oi => oi.OrderId == orderId)
                .Select(oi => new { oi.ProductId, oi.Quantity })
                .ToListAsync();

            return ImzaUret(kalemler.Select(k => (k.ProductId, k.Quantity)));
        }

        // Listede sipariş başına kaç ürün adı gösterilecek?
        //
        // ⚠️ Kalemlerin TAMAMI gönderilmiyor. Ekranda satır başına iki
        // satırlık yer var; 20 kalemlik bir siparişin hepsini yollamak
        // hiç çizilmeyecek veriyi ağdan geçirmek olurdu.
        // ("Liste ucu özet, detay ucu tam veri" — admin sipariş
        //  listesindeki desenin aynısı.)
        private const int OnizlemeUrunSayisi = 3;

        // 🟡 GET /api/hizli-siparisler — kaydettiğim siparişler
        [HttpGet]
        public async Task<IActionResult> Listele()
        {
            var userId = GetUserId();

            var liste = await _context.HizliSiparisler
                .Where(h => h.UserId == userId)

                // En son kaydedilen en üstte: müşteri az önce
                // kaydettiğini listenin başında görmeli.
                //
                // ⚠️ Siparişin TARİHİNE göre değil, KAYIT tarihine göre.
                // İkisi farklı sorular: "ne zaman sipariş verdim" ile
                // "ne zaman kaydettim". Bu ekran ikincisinin listesi.
                .OrderByDescending(h => h.CreatedAt)
                .ThenByDescending(h => h.Id)

                .Join(_context.Orders,
                      h => h.OrderId,
                      o => o.Id,
                      (h, o) => new { h, o })

                .Select(x => new
                {
                    orderId = x.o.Id,
                    siparisNo = x.o.OrderNumber,
                    siparisTarihi = x.o.CreatedAt,
                    kayitTarihi = x.h.CreatedAt,
                    toplam = x.o.Total,

                    // Kaç çeşit / kaç adet — "3 üründen 5 adet" diye
                    // gösterilecek.
                    urunCesidi = _context.OrderItems.Count(oi => oi.OrderId == x.o.Id),

                    toplamAdet = _context.OrderItems
                        .Where(oi => oi.OrderId == x.o.Id)
                        .Sum(oi => (int?)oi.Quantity) ?? 0,

                    // ⚠️ ÜRÜN ADI KALEMİN İÇİNDEN, Products'tan DEĞİL.
                    //
                    // OrderItem.ProductName sipariş anında donduruldu.
                    // Products'a JOIN atsaydık iki sorun çıkardı:
                    // ürün silinmişse INNER JOIN satırı düşürür ve
                    // isim listesi eksik kalırdı; adı değişmişse
                    // müşteri sipariş verdiği ürünü tanıyamazdı.
                    //
                    // Bu alt sorgu ana SQL'e gömülüyor — N+1 yok.
                    urunler = _context.OrderItems
                        .Where(oi => oi.OrderId == x.o.Id)
                        .Select(oi => oi.ProductName)
                        .Take(OnizlemeUrunSayisi)
                        .ToList()
                })
                .ToListAsync();

            return Ok(liste);
        }

        // 🟡 POST /api/hizli-siparisler/5 — siparişi kaydet
        [HttpPost("{orderId}")]
        public async Task<IActionResult> Kaydet(int orderId)
        {
            var userId = GetUserId();

            // ⚠️ SAHİPLİK KONTROLÜ SORGUNUN İÇİNDE.
            //
            // Ayrı bir if olarak yazsaydık unutulabilirdi. Olmasaydı:
            // giriş yapmış herhangi biri id'leri deneyerek BAŞKASININ
            // siparişini kendi listesine kaydedebilir ve sipariş
            // numarasıyla tutarını öğrenebilirdi (IDOR).
            var siparisVar = await _context.Orders
                .AnyAsync(o => o.Id == orderId && o.UserId == userId);

            if (!siparisVar)
            {
                // ⚠️ "Bu senin değil" demiyoruz, "yok" diyoruz.
                // İlki o id'de bir siparişin VAR olduğunu sızdırırdı.
                return NotFound(new { mesaj = "Sipariş bulunamadı!" });
            }

            var imza = await SiparisImzasiAsync(orderId);

            // ⚠️ ÖN KONTROL: aynı İÇERİKTE başka bir kayıt var mı?
            //
            // Garanti değil (iki eşzamanlı istek ikisi de "yok"
            // görebilir) — asıl koruma aşağıdaki benzersiz indeks. Ama
            // %99 durumu ucuza halleder ve müşteriye hangi siparişin
            // zaten kayıtlı olduğunu SÖYLEYEBİLİR. İstisna yolundan
            // gelseydik elimizde o bilgi olmazdı.
            var mevcut = await _context.HizliSiparisler
                .Where(h => h.UserId == userId && h.IcerikImzasi == imza)
                .Join(_context.Orders, h => h.OrderId, o => o.Id, (h, o) => o.OrderNumber)
                .FirstOrDefaultAsync();

            if (mevcut != null)
            {
                // ⚠️ HATA DEĞİL, 200.
                //
                // Müşterinin istediği sonuç zaten sağlanmış: bu içerik
                // hızlı siparişlerinde. Ona kırmızı bir hata göstermek,
                // olmayan bir sorunu varmış gibi sunmak olurdu.
                //
                // kayitli: true → ekrandaki buton "kayıtlı" durumuna
                // geçiyor; mesaj neden ikinci bir satır eklenmediğini
                // anlatıyor.
                return Ok(new
                {
                    mesaj = $"Aynı içerikte bir hızlı siparişin zaten var ({mevcut}).",
                    kayitli = true,

                    // ⚠️ İstemci bu durumu METNE BAKMADAN ayırt edebilsin.
                    // Mesaj yarın düzeltilirse metne bakan kod kırılırdı;
                    // kod sabit kalır. (KUPON_GECERSIZ ile aynı gerekçe.)
                    mevcuttu = true
                });
            }

            _context.HizliSiparisler.Add(new HizliSiparis
            {
                UserId = userId,
                OrderId = orderId,
                IcerikImzasi = imza,
                CreatedAt = DateTime.UtcNow
            });

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // ⚠️ İKİ benzersiz indeksten biri devreye girdi:
                //   (UserId, OrderId)      → bu sipariş zaten kayıtlı
                //   (UserId, IcerikImzasi) → bu içerik zaten kayıtlı
                //
                // Hangisi olduğunu ayırt etmiyoruz: müşteri açısından
                // ikisi de aynı şey — istediği içerik listesinde.
                //
                // Bu bir HATA DEĞİL. İki kez basmak ya da iki cihazdan
                // aynı anda kaydetmek hata mesajı görmemeli.
                // (İdempotentlik — sipariş oluşturmadaki desenin aynısı.)
                return Ok(new
                {
                    mesaj = "Bu sipariş zaten hızlı siparişlerinde.",
                    kayitli = true,
                    mevcuttu = true
                });
            }

            return Ok(new
            {
                mesaj = "Sipariş hızlı siparişlerine eklendi.",
                kayitli = true,
                mevcuttu = false
            });
        }

        // 🟡 DELETE /api/hizli-siparisler/5 — kaydı kaldır
        //
        // ⚠️ Sipariş SİLİNMİYOR, yalnızca işaret kaldırılıyor.
        // Sipariş bir ticari kayıt; bu uç ona hiç dokunmuyor.
        [HttpDelete("{orderId}")]
        public async Task<IActionResult> Kaldir(int orderId)
        {
            var userId = GetUserId();

            // ⚠️ Sahiplik yine sorgunun içinde: başkasının kaydını
            // silmek imkânsız. ExecuteDeleteAsync tek SQL cümlesi —
            // önce çekip sonra silmeye gerek yok.
            var silinen = await _context.HizliSiparisler
                .Where(h => h.OrderId == orderId && h.UserId == userId)
                .ExecuteDeleteAsync();

            // ⚠️ Kayıt yoksa da 200 dönüyoruz.
            //
            // İşin SONUCU istenen durum: sipariş listede değil. Zaten
            // silinmiş bir kaydı tekrar silmeye çalışan istemciye hata
            // göstermek, iki sekmeden aynı butona basan kullanıcıyı
            // cezalandırmak olurdu. (Oturum iptalindeki desen.)
            return Ok(new
            {
                mesaj = silinen > 0
                    ? "Sipariş hızlı siparişlerinden çıkarıldı."
                    : "Bu sipariş zaten hızlı siparişlerinde değil.",
                kayitli = false
            });
        }
    }
}
