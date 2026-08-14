using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.DTOs;
using ETicaretAPI.Models;   // ⭐ YENİ — denetim yardımcısı Product alıyor
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

        // ⭐ YENİ — denetim kaydı.
        //
        // ⚠️ Bu controller doğrudan FİYAT DEĞİŞTİRİYOR ve denetimsiz
        // kalması sistemdeki en riskli boşluktu: bir ürünün fiyatının
        // kim tarafından, ne zaman düşürüldüğünün hiçbir kaydı yoktu.
        private readonly DenetimKaydi _denetim;

        public AdminIndirimlerController(AppDbContext context, DenetimKaydi denetim)
        {
            _context = context;
            _denetim = denetim;
        }


        // Token'dan admin kimliği. ⚠️ Uç [Authorize] altında, yani her
        // zaman dolu; 0'a düşmek yalnızca savunma amaçlı.
        private int AdminId()
        {
            var talep = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);

            return talep != null && int.TryParse(talep.Value, out var id) ? id : 0;
        }


        // Fiyat değişikliğini denetime yazar (context'e EKLER, kaydetmez).
        //
        // ⚠️ Tek yerde: dört uç da (tekil/toplu uygula, tekil/toplu
        // kaldır) aynı şeyi kaydediyor ve biçimleri ayrışmamalı.
        private Task DenetimeYazAsync(
            Product urun,
            string islem,
            decimal oncekiFiyat,
            decimal? oncekiEskiFiyat)
        {
            var adminId = AdminId();

            return _denetim.EkleAsync(
                yapanId: adminId,
                hedefId: adminId,
                hedefAd: DenetimEtiketi.Urun(urun.Id, urun.Name),
                islem: islem,

                // ⚠️ eskiFiyat da yazılıyor: indirim "fiyatı düşür +
                // eski fiyatı göster" olarak iki alan birden değiştiriyor.
                // Yalnızca fiyatı kaydetseydik indirimin kaldırılıp
                // kaldırılmadığı kayıttan anlaşılmazdı.
                eski: DenetimDegeri.Yaz(new Dictionary<string, object?>
                {
                    ["fiyat"] = oncekiFiyat,
                    ["eskiFiyat"] = oncekiEskiFiyat
                }),
                yeni: DenetimDegeri.Yaz(new Dictionary<string, object?>
                {
                    ["fiyat"] = urun.Price,
                    ["eskiFiyat"] = urun.EskiFiyat
                }));
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

            // ⚠️ Önceki değerler Uygula() ÇAĞRILMADAN önce alınıyor;
            // sonrasında bellekte eski fiyat kalmıyor.
            var oncekiFiyat = urun.Price;
            var oncekiEskiFiyat = urun.EskiFiyat;

            IndirimUygulayici.Uygula(urun, sonuc);

            await DenetimeYazAsync(
                urun, DenetimIslemi.IndirimUygulandi, oncekiFiyat, oncekiEskiFiyat);

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

            var oncekiFiyat = urun.Price;
            var oncekiEskiFiyat = urun.EskiFiyat;

            if (!IndirimUygulayici.Kaldir(urun))
            {
                // ⚠️ 400 değil 200: istenen son durum (indirimsiz ürün)
                // zaten sağlanmış. İki sekmede açık panelde ikinci
                // tıklama kırmızı hata göstermemeli.
                //
                // ⚠️ Denetim kaydı da YAZILMIYOR: hiçbir şey değişmedi,
                // yazsaydık defter değişiklik yaratmayan tıklamalarla
                // dolardı.
                return Ok(new { mesaj = "Üründe zaten indirim yok.", fiyat = urun.Price });
            }

            await DenetimeYazAsync(
                urun, DenetimIslemi.IndirimKaldirildi, oncekiFiyat, oncekiEskiFiyat);

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

                var oncekiFiyat = urun.Price;
                var oncekiEskiFiyat = urun.EskiFiyat;

                IndirimUygulayici.Uygula(urun, sonuc);

                // ⚠️ ÜRÜN BAŞINA BİR SATIR — özet satır DEĞİL.
                //
                // "200 ürüne %20 indirim yapıldı" diyen tek bir kayıt
                // kısa görünürdü ama denetimin asıl sorusunu ("BU ürünün
                // fiyatını kim düşürdü") cevaplayamazdı. Toplu işlemler
                // tam olarak tek tek yapılanları gizlediği için riskli;
                // kayıt da o yüzden tek tek tutuluyor.
                await DenetimeYazAsync(
                    urun, DenetimIslemi.IndirimUygulandi, oncekiFiyat, oncekiEskiFiyat);

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

            // ⚠️ Count(IndirimUygulayici.Kaldir) yerine açık döngü:
            // denetim kaydı için her ürünün ÖNCEKİ fiyatı lazım ve
            // LINQ ifadesinin içinde onu yakalamanın yolu yok.
            var kaldirilan = 0;

            foreach (var urun in urunler)
            {
                var oncekiFiyat = urun.Price;
                var oncekiEskiFiyat = urun.EskiFiyat;

                if (!IndirimUygulayici.Kaldir(urun))
                {
                    continue;
                }

                await DenetimeYazAsync(
                    urun, DenetimIslemi.IndirimKaldirildi, oncekiFiyat, oncekiEskiFiyat);

                kaldirilan++;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                mesaj = $"{kaldirilan} üründe indirim kaldırıldı.",
                kaldirilan,
            });
        }
    }
}
