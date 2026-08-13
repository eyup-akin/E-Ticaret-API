using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
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

            _context.HizliSiparisler.Add(new HizliSiparis
            {
                UserId = userId,
                OrderId = orderId,
                CreatedAt = DateTime.UtcNow
            });

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // ⚠️ (UserId, OrderId) benzersiz indeksi devreye girdi:
                // sipariş zaten kayıtlı.
                //
                // Bu bir HATA DEĞİL. Müşteri açısından sonuç istediği
                // durum: sipariş listesinde. İki kez basmak ya da iki
                // cihazdan aynı anda kaydetmek hata mesajı görmemeli.
                // (İdempotentlik — sipariş oluşturmadaki desenin aynısı.)
                return Ok(new { mesaj = "Bu sipariş zaten hızlı siparişlerinde.", kayitli = true });
            }

            return Ok(new { mesaj = "Sipariş hızlı siparişlerine eklendi.", kayitli = true });
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
