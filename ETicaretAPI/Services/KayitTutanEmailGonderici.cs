using ETicaretAPI.Data;
using ETicaretAPI.Models;

namespace ETicaretAPI.Services
{
    // ⭐ YENİ — E-POSTA GÖNDERİM KAYDINI YAZAN SARMALAYICI
    //
    // Gerçek göndericiyi (Brevo ya da konsol) sarar, sonucu EmailKayitlari
    // tablosuna yazar ve istisnayı olduğu gibi yukarı bırakır.
    //
    // ⚠️ NEDEN SARMALAYICI (decorator), NEDEN ÇAĞRI YERLERİNE KOD DEĞİL?
    //
    // 13 çağrı yeri var. Her birine "gönder, sonra kaydet" yazsaydık
    // birinde unutmak yeterliydi — ve unutulan yer, gitmeyen bir maili
    // kaydetmeyen yer olurdu. Sarmalayıcı hiçbir yolun atlayamayacağı bir
    // nokta yaratıyor (StokDefteri ve DenetimKaydi ile aynı gerekçe).
    //
    // ⚠️⚠️ KENDİ KAPSAMINI (scope) AÇIYOR — İSTEĞİN DbContext'İNİ KULLANMAZ.
    //
    // İstekten gelen DbContext'e yazıp SaveChanges deseydik, o context'te
    // bekleyen BAŞKA değişiklikler de birlikte commit olurdu. Örnek: sipariş
    // iptali maili gönderilirken henüz kaydedilmemiş bir değişiklik varsa,
    // mail kaydı onu da yazardı — kimsenin istemediği bir yan etki.
    //
    // Ayrıca e-posta gönderimi zaten commit SONRASI çalışıyor; kaydın
    // tetikleyen işlemle aynı transaction'da olmasına gerek yok.
    // (Denetim kaydında karar TAM TERSİ ve gerekçesi orada yazılı.)
    public class KayitTutanEmailGonderici : IEmailGonderici
    {
        private readonly IEmailGonderici _ic;
        private readonly IServiceScopeFactory _kapsamlar;
        private readonly ILogger<KayitTutanEmailGonderici> _log;

        public KayitTutanEmailGonderici(
            IEmailGonderici ic,
            IServiceScopeFactory kapsamlar,
            ILogger<KayitTutanEmailGonderici> log)
        {
            _ic = ic;
            _kapsamlar = kapsamlar;
            _log = log;
        }

        public async Task<string?> GonderAsync(
            string aliciEmail, string konu, string govdeHtml, string olayAdi)
        {
            // ⚠️ ALICI ADRESİ YOKSA DENEMEYE BİLE GEREK YOK.
            // (Hesabı kapatılmış kullanıcıların e-postası maskeleniyor.)
            //
            // Kayıt YİNE DE yazılıyor: "gitmedi" bilgisi panelde
            // görünmeli. Gövde saklanmıyor — alıcısı olmayan bir mail
            // tekrar gönderilemez, saklamak boşuna yer kaplardı.
            if (string.IsNullOrWhiteSpace(aliciEmail))
            {
                _log.LogWarning(
                    "E-posta atlandı — alıcı adresi boş. Olay: {Olay}", olayAdi);

                await KaydetAsync(new EmailKaydi
                {
                    Alici = "",
                    Konu = Kirp(konu, 250),
                    Olay = Kirp(olayAdi, 60),
                    Basarili = false,
                    HataMesaji = "Alıcı adresi boş — gönderim denenmedi.",
                    CreatedAt = DateTime.UtcNow
                });

                return null;
            }

            try
            {
                var mesajId = await _ic.GonderAsync(aliciEmail, konu, govdeHtml, olayAdi);

                // ⚠️ Başarıda GÖVDE YAZILMIYOR. Yazsaydık sipariş
                // içeriğini ikinci kez arşivlemiş olurduk: hem gereksiz
                // yer hem kişisel verinin çoğaltılması.
                await KaydetAsync(new EmailKaydi
                {
                    Alici = Kirp(aliciEmail, 256),
                    Konu = Kirp(konu, 250),
                    Olay = Kirp(olayAdi, 60),
                    Basarili = true,
                    SaglayiciMesajId = mesajId,
                    CreatedAt = DateTime.UtcNow
                });

                return mesajId;
            }
            catch (Exception hata)
            {
                // ⚠️ GÖVDE YALNIZCA BURADA SAKLANIYOR.
                //
                // "Gitmedi" bilgisi tek başına işe yaramaz; yanında bir
                // düzeltme yolu olmalı. Tekrar gönderim başarılı olunca
                // gövde siliniyor (bkz. AdminLoglarController).
                await KaydetAsync(new EmailKaydi
                {
                    Alici = Kirp(aliciEmail, 256),
                    Konu = Kirp(konu, 250),
                    Olay = Kirp(olayAdi, 60),
                    Basarili = false,
                    HataMesaji = Kirp(hata.Message, 1000),
                    GovdeHtml = govdeHtml,
                    CreatedAt = DateTime.UtcNow
                });

                // ⚠️ İSTİSNA YUTULMUYOR, YUKARI BIRAKILIYOR.
                // Çağıran taraf GuvenliGonderAsync ile zaten korunuyor ve
                // orada ILogger'a yazılıyor. Burada yutsaydık hata İKİ
                // katmanda birden kaybolurdu.
                throw;
            }
        }

        // Kaydı KENDİ DbContext'inde yazar.
        //
        // ⚠️ Bu metot HİÇBİR KOŞULDA istisna fırlatmamalı: kayıt yazma
        // hatası, gitmiş bir maili "gitmedi" göstermeye ya da gitmemiş
        // bir hatayı büsbütün gizlemeye yol açardı. Log'a düşürüp
        // devam ediyoruz.
        private async Task KaydetAsync(EmailKaydi kayit)
        {
            try
            {
                using var kapsam = _kapsamlar.CreateScope();

                var context = kapsam.ServiceProvider.GetRequiredService<AppDbContext>();

                context.EmailKayitlari.Add(kayit);
                await context.SaveChangesAsync();
            }
            catch (Exception hata)
            {
                _log.LogError(hata,
                    "E-posta kaydı yazılamadı. Olay: {Olay}", kayit.Olay);
            }
        }

        // Kolon sınırlarını aşan metni kırpar.
        //
        // ⚠️ Kırpmasaydık uzun bir konu ya da hata mesajı SaveChanges'i
        // patlatırdı ve kayıt hiç yazılmazdı — kırpılmış bir kayıt,
        // olmayan kayıttan iyidir.
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
