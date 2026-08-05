using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ETicaretAPI.Services;

namespace ETicaretAPI.Controllers
{
    [ApiController]
    [Route("api/ayarlar")]
    public class AyarlarController : ControllerBase
    {
        private readonly MagazaAyarlari _ayarlar;

        public AyarlarController(MagazaAyarlari ayarlar)
        {
            _ayarlar = ayarlar;
        }

        // 🟢 GET /api/ayarlar
        //
        // Mobil uygulamanın açılışta bir kez çektiği ayarlar.
        //
        // ⚠️ HERKESE AÇIK — [AllowAnonymous].
        // Misafir kullanıcı da sepet kullanabiliyor ve kargo
        // ücretini görmesi gerekiyor. Giriş şartı koysaydık
        // misafir sepette "kargo: ?" görürdü.
        //
        // ⚠️ SADECE MÜŞTERİYİ İLGİLENDİREN ALANLAR DÖNÜYOR.
        //
        // StokAzEsigi burada YOK — bilerek. Müşteri "5'in altı az
        // sayılıyor" bilgisini bilmemeli; Aşama 5.3'te stok durumu
        // (yok/az/var) SUNUCUDA hesaplanıp gönderilecek, ham eşik
        // hiç gitmeyecek.
        //
        // "JSON'da giden her şey herkese açıktır." Ekranda
        // göstermemek yetmez — ProductDto.Cost'ta verdiğimiz
        // kararın aynısı.
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Getir()
        {
            return Ok(new
            {
                magazaAdi = _ayarlar.Ad,

                kargoUcreti = _ayarlar.KargoUcreti,
                ucretsizKargoLimiti = _ayarlar.UcretsizKargoLimiti,

                // Mobilin "+" butonunu doğru yerde durdurabilmesi için.
                // Sunucu zaten kırpıyor ama arayüz sınırı önceden
                // bilirse kullanıcıya "en fazla 99 adet" diyebilir —
                // sessizce kırpılan bir istekten iyidir.
                sepetMaksAdet = _ayarlar.SepetMaksAdet
            });
        }


        // 🔴 GET /api/ayarlar/yonetim
        //
        // Admin panelinin ihtiyaç duyduğu, müşteriye kapalı ayarlar.
        //
        // NEDEN AYRI BİR UÇ, NEDEN YUKARIDAKİNE EKLEMEDİK?
        // "Liste ucu özet, detay ucu tam" deseninin yetki
        // versiyonu: aynı uçtan role göre farklı alanlar döndürmek
        // mümkün ama okuması zor olur ve bir gün yanlış dala düşer.
        // Ayrı uç, ayrı yetki — hangi verinin kime gittiği
        // rotaya bakınca anlaşılıyor.
        [Authorize(Roles = "admin")]
        [HttpGet("yonetim")]
        public IActionResult YonetimAyarlari()
        {
            return Ok(new
            {
                stokAzEsigi = _ayarlar.StokAzEsigi,
                kdvVarsayilanOran = _ayarlar.KdvVarsayilanOran,
                kargoUcreti = _ayarlar.KargoUcreti,
                ucretsizKargoLimiti = _ayarlar.UcretsizKargoLimiti,
                sepetMaksAdet = _ayarlar.SepetMaksAdet,
                siparisNoOneki = _ayarlar.SiparisNoOneki
            });
        }
    }
}