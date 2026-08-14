using ETicaretAPI.Data;
using ETicaretAPI.Models;

namespace ETicaretAPI.Services
{
    // ⭐ YENİ — GİRİŞ VE HATA KAYITLARINI YAZAN SERVİS
    //
    // ⚠️ İKİSİ DE KENDİ KAPSAMINDA (scope) YAZILIYOR VE HATA YUTULUYOR.
    // Denetim kaydındaki kararın TAM TERSİ; sebebi de tam tersi:
    //
    //   • Denetim → tetikleyen işlemle aynı transaction. Kayıt
    //     yazılamazsa işlem de geri alınmalı, çünkü izlenmeyen bir fiyat
    //     değişikliği başarısız olandan kötüdür.
    //
    //   • Giriş   → ortada transaction yok. Kayıt yazılamadı diye
    //     kullanıcının girişini reddetmek anlamsız olurdu.
    //
    //   • Hata    → tetikleyen transaction ZATEN geri alınıyor; kaydı
    //     ona bağlamak, kaydın da silinmesi demekti. Üstelik yutmazsak
    //     hata döngüsü başlar: log yazarken çıkan hata tekrar log
    //     yazmaya çalışır.
    //
    // ⚠️ NEDEN Singleton + IServiceScopeFactory?
    // Hata kaydı middleware'den geliyor ve orada isteğin DbContext'i ya
    // yok ya da bozulmuş durumda (patlayan bir transaction'ın içinde).
    // Kendi kapsamını açmak tek güvenli yol.
    public class SistemGunlugu
    {
        private readonly IServiceScopeFactory _kapsamlar;
        private readonly ILogger<SistemGunlugu> _log;

        public SistemGunlugu(IServiceScopeFactory kapsamlar, ILogger<SistemGunlugu> log)
        {
            _kapsamlar = kapsamlar;
            _log = log;
        }

        /// <summary>
        /// Giriş denemesini kaydeder. ⚠️ Şifre HİÇBİR koşulda yazılmaz.
        /// </summary>
        public Task GirisYazAsync(string? email, string sonuc, string? ip)
        {
            return YazAsync(context => context.GirisKayitlari.Add(new GirisKaydi
            {
                // ⚠️ Kırpma şart: e-posta alanı 256 karakter ve giriş
                // ekranına istediğini yazabilen biri daha uzun gönderebilir.
                // Kırpmasaydık kayıt hiç yazılmazdı.
                Email = Kirp(email, 256),
                Sonuc = sonuc,
                IpAdresi = ip,
                CreatedAt = DateTime.UtcNow
            }));
        }

        /// <summary>
        /// 500 dönen bir isteği kaydeder.
        /// </summary>
        public Task HataYazAsync(
            string yol,
            string yontem,
            string mesaj,
            string? yiginIzi,
            int? kullaniciId,
            string? ip)
        {
            return YazAsync(context => context.HataKayitlari.Add(new HataKaydi
            {
                Yol = Kirp(yol, 300),
                Yontem = Kirp(yontem, 10),
                Mesaj = Kirp(mesaj, 1000),
                YiginIzi = yiginIzi,
                KullaniciId = kullaniciId,
                IpAdresi = ip,
                CreatedAt = DateTime.UtcNow
            }));
        }

        // Kendi kapsamında yazar; hiçbir koşulda istisna fırlatmaz.
        private async Task YazAsync(Action<AppDbContext> ekle)
        {
            try
            {
                using var kapsam = _kapsamlar.CreateScope();

                var context = kapsam.ServiceProvider.GetRequiredService<AppDbContext>();

                ekle(context);
                await context.SaveChangesAsync();
            }
            catch (Exception hata)
            {
                // ⚠️ ILogger'a düşüyor — "yutmak" ile "loglayıp devam
                // etmek" farklı şeyler.
                _log.LogError(hata, "Sistem kaydı yazılamadı.");
            }
        }

        private static string Kirp(string? metin, int sinir)
        {
            if (string.IsNullOrEmpty(metin))
            {
                return string.Empty;
            }

            return metin.Length <= sinir ? metin : metin[..sinir];
        }
    }
}
