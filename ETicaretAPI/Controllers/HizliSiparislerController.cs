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
    // ⚠️ LİSTE UCU (GET) HENÜZ YOK. Ekran bir sonraki aşamada
    // yazılacak; tüketicisi olmayan bir uç yazmak, yarın ekran
    // yazılırken "acaba bu cevap şekli doğru mu" diye tartışılacak
    // bir varsayım bırakmak olurdu.
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
