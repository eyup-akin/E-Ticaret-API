using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.DTOs;
using ETicaretAPI.Services;

namespace ETicaretAPI.Controllers
{
    // ⭐ YENİ — İNDİRİM YÖNETİMİ (admin)
    //
    // Panelin "İndirimler" sayfası buradan besleniyor.
    //
    // ⚠️ ÜRÜN LİSTESİ BURADA YOK. Sayfa listeyi mevcut
    // `GET /api/products` ucundan çekiyor; oraya yalnızca
    // `sadeceIndirimli` süzgeci eklendi. İkinci bir liste ucu yazmak,
    // görünürlük kilidini, maliyet gizlemeyi, resim ve puan doldurmayı
    // ikinci kez yazmak olurdu.
    [Route("api/admin/indirimler")]
    [ApiController]
    [Authorize(Roles = "admin")]
    public class AdminIndirimlerController : ControllerBase
    {
        // Tek istekte kaç ürüne indirim uygulanabilir?
        //
        // ⚠️ Sınır var çünkü hepsi tek transaction'da yazılıyor ve
        // sınırsız bir liste hem isteği hem kilidi uzatırdı. 200,
        // "bir kategoriyi toptan indir" ihtiyacını rahatça karşılıyor.
        private const int TopluMaksAdet = 200;

        private readonly AppDbContext _context;

        public AdminIndirimlerController(AppDbContext context)
        {
            _context = context;
        }

        // 🔴 PUT /api/admin/indirimler/5   { tip: "yuzde", deger: 20 }
        [HttpPut("{urunId}")]
        public async Task<IActionResult> Uygula(int urunId, [FromBody] IndirimUygulaDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var urun = await _context.Products.FirstOrDefaultAsync(p => p.Id == urunId);

            if (urun == null)
            {
                return NotFound(new { mesaj = "Ürün bulunamadı." });
            }

            var (sonuc, hata) = IndirimUygulayici.Hesapla(urun, dto.Tip, dto.Deger);

            if (sonuc == null)
            {
                return BadRequest(new { mesaj = hata });
            }

            IndirimUygulayici.Uygula(urun, sonuc);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                mesaj = "İndirim uygulandı.",
                yeniFiyat = sonuc.YeniFiyat,
                eskiFiyat = sonuc.EskiFiyat,

                // ⚠️ Uyarı, hata DEĞİL: işlem yapıldı. Panel bunu
                // gördüğünde sarı bir not gösteriyor. Engellemiyoruz
                // çünkü zararına satış bilinçli bir kampanya olabilir.
                maliyetinAltinda = sonuc.MaliyetinAltinda,
            });
        }

        // 🔴 DELETE /api/admin/indirimler/5 — indirimi kaldır
        [HttpDelete("{urunId}")]
        public async Task<IActionResult> Kaldir(int urunId)
        {
            var urun = await _context.Products.FirstOrDefaultAsync(p => p.Id == urunId);

            if (urun == null)
            {
                return NotFound(new { mesaj = "Ürün bulunamadı." });
            }

            if (!IndirimUygulayici.Kaldir(urun))
            {
                // ⚠️ 400 değil 200: istenen son durum (indirimsiz ürün)
                // zaten sağlanmış. İki sekmede açık panelde ikinci
                // tıklama kırmızı hata göstermemeli.
                return Ok(new { mesaj = "Üründe zaten indirim yok.", fiyat = urun.Price });
            }

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "İndirim kaldırıldı.", fiyat = urun.Price });
        }

        // 🔴 POST /api/admin/indirimler/toplu
        //
        // Seçili ürünlere aynı indirimi uygular.
        //
        // ⚠️ KISMİ BAŞARI KABUL EDİLİYOR. 30 üründen 2'si (fiyatı sıfır,
        // arşivli, oran fiyattan büyük) elenirse diğer 28'i geri
        // almıyoruz — admin bunu tek tek yapsaydı da 28'i geçerdi.
        // Hangilerinin neden atlandığı cevapta AÇIKÇA dönüyor; sessizce
        // "hepsi oldu" demek yanlış bilgi olurdu.
        [HttpPost("toplu")]
        public async Task<IActionResult> Toplu([FromBody] TopluIndirimDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var idler = dto.UrunIdleri.Distinct().ToList();

            if (idler.Count > TopluMaksAdet)
            {
                return BadRequest(new
                {
                    mesaj = $"Tek seferde en fazla {TopluMaksAdet} ürüne indirim uygulanabilir."
                });
            }

            var urunler = await _context.Products
                .Where(p => idler.Contains(p.Id))
                .ToListAsync();

            var uygulanan = 0;
            var maliyetUyarisi = 0;
            var atlananlar = new List<object>();

            foreach (var urun in urunler)
            {
                var (sonuc, hata) = IndirimUygulayici.Hesapla(urun, dto.Tip, dto.Deger);

                if (sonuc == null)
                {
                    atlananlar.Add(new { urun.Id, urun.Name, sebep = hata });
                    continue;
                }

                IndirimUygulayici.Uygula(urun, sonuc);
                uygulanan++;

                if (sonuc.MaliyetinAltinda)
                {
                    maliyetUyarisi++;
                }
            }

            // ⚠️ Tek SaveChanges: 200 ürün için 200 gidiş-dönüş yerine
            // bir yazma. Aynı zamanda atomik — yarısı yazılmış bir
            // kampanya kalmıyor.
            await _context.SaveChangesAsync();

            return Ok(new
            {
                mesaj = uygulanan == urunler.Count
                    ? $"{uygulanan} ürüne indirim uygulandı."
                    : $"{uygulanan} ürüne indirim uygulandı, {atlananlar.Count} ürün atlandı.",
                uygulanan,
                maliyetUyarisi,
                atlananlar,
            });
        }

        // 🔴 POST /api/admin/indirimler/toplu-kaldir
        [HttpPost("toplu-kaldir")]
        public async Task<IActionResult> TopluKaldir([FromBody] List<int> idler)
        {
            if (idler == null || idler.Count == 0)
            {
                return BadRequest(new { mesaj = "Ürün seçilmedi." });
            }

            if (idler.Count > TopluMaksAdet)
            {
                return BadRequest(new
                {
                    mesaj = $"Tek seferde en fazla {TopluMaksAdet} ürünün indirimi kaldırılabilir."
                });
            }

            var urunler = await _context.Products
                .Where(p => idler.Contains(p.Id))
                .ToListAsync();

            var kaldirilan = urunler.Count(IndirimUygulayici.Kaldir);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                mesaj = $"{kaldirilan} üründe indirim kaldırıldı.",
                kaldirilan,
            });
        }
    }
}
