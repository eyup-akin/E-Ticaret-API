using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;

namespace ETicaretAPI.Services
{
    // ⭐ YENİ — LOG TEMİZLİĞİ (gecelik Hangfire işi)
    //
    // Hiçbir log sonsuza kadar tutulmaz: tablo şişer, sayfa yavaşlar,
    // yedek büyür. Yaşı geçen satırlar siliniyor.
    //
    // ⚠️⚠️ YALNIZCA LOG TABLOLARI. Siparişler, ödemeler, iadeler ve
    // sözleşme onayları ticari/yasal kayıt ve kendi kurallarıyla
    // duruyorlar — hesap kapatmada bile silinmiyor, anonimleşiyorlar.
    // Bu iş onlara HİÇ dokunmamalı.
    //
    // ⚠️ ARŞİVLEME YOK (bilinçli): 6 ayı geçen denetim kaydı doğrudan
    // siliniyor. Arşiv yazmak dosya biçimi, saklama yeri, o dosyanın
    // yedeklenmesi ve arşivin de bir gün temizlenmesi gibi bir zincir
    // açardı. İhtiyaç doğarsa çevrilecek düğme arşiv değil, SÜRENİN
    // KENDİSİ (appsettings → Loglar).
    //
    // ⚠️ NEDEN HANGFIRE, NEDEN BackgroundService DEĞİL?
    // Hangfire zaten kurulu ve tekrarlayan bir iş çalıştırıyor
    // (stok-bildirimleri). İkinci bir zamanlama mekanizması, bir iş
    // çalışmadığında iki yere bakmak demekti. Ayrıca sunucu bir ev
    // bilgisayarı: 03:00'te makine kapalıysa Timer tabanlı bir worker o
    // günü ATLAR, Hangfire kaçırılan işi açılışta çalıştırır.
    public class LogTemizlikServisi
    {
        private readonly AppDbContext _context;
        private readonly LogAyarlari _ayarlar;
        private readonly ILogger<LogTemizlikServisi> _log;

        public LogTemizlikServisi(
            AppDbContext context,
            LogAyarlari ayarlar,
            ILogger<LogTemizlikServisi> log)
        {
            _context = context;
            _ayarlar = ayarlar;
            _log = log;
        }

        // ------------------------------------------------------------
        //  Hangfire'ın çağırdığı giriş noktası.
        //  ⚠️ public ve parametresiz olmalı — Hangfire metodu adıyla
        //  serileştirip yansımayla (reflection) çağırıyor.
        // ------------------------------------------------------------
        //
        // ⚠️⚠️ PARTİ PARTİ SİLİNİYOR — TEK SEFERDE DEĞİL.
        //
        // İlk çalıştırmada tabloda yılların kaydı birikmiş olabilir;
        // hepsini tek cümlede silmek uzun bir kilit tutar ve panel o
        // sırada kilitlenir. Her turda üst sınır kadar siliniyor, kalanı
        // bir sonraki gece devam ediyor.
        //
        // ⚠️ ExecuteDeleteAsync: tek SQL cümlesi, change tracker'a
        // yüklemeden siler. Belleğe çekip RemoveRange demek, altı aylık
        // denetim kaydını RAM'e yüklemek olurdu.
        //
        // ⚠️ OrderBy şart: sırasız bir Take deterministik değildir,
        // SQL Server herhangi bir satır seçebilir. Biz EN ESKİYİ silmek
        // istiyoruz — aksi hâlde parti sınırı yüzünden hep aynı eski
        // satırlar hayatta kalabilirdi.
        public async Task EskileriSilAsync()
        {
            var parti = _ayarlar.TemizlikPartiBoyutu;
            var simdi = DateTime.UtcNow;

            // ⚠️ Sınır tarihleri döngü DIŞINDA hesaplanıyor: sorgunun
            // içinde AddDays çağırmak, EF'in onu her satır için SQL'e
            // çevirmesine yol açardı.
            var denetimSiniri = simdi.AddDays(-_ayarlar.DenetimGun);
            var emailSiniri = simdi.AddDays(-_ayarlar.EmailGun);
            var girisSiniri = simdi.AddDays(-_ayarlar.GirisGun);
            var hataSiniri = simdi.AddDays(-_ayarlar.HataGun);

            var denetim = await _context.AuditLogs
                .Where(x => x.CreatedAt < denetimSiniri)
                .OrderBy(x => x.CreatedAt)
                .Take(parti)
                .ExecuteDeleteAsync();

            var eposta = await _context.EmailKayitlari
                .Where(x => x.CreatedAt < emailSiniri)
                .OrderBy(x => x.CreatedAt)
                .Take(parti)
                .ExecuteDeleteAsync();

            var giris = await _context.GirisKayitlari
                .Where(x => x.CreatedAt < girisSiniri)
                .OrderBy(x => x.CreatedAt)
                .Take(parti)
                .ExecuteDeleteAsync();

            var hata = await _context.HataKayitlari
                .Where(x => x.CreatedAt < hataSiniri)
                .OrderBy(x => x.CreatedAt)
                .Take(parti)
                .ExecuteDeleteAsync();

            var toplam = denetim + eposta + giris + hata;

            if (toplam > 0)
            {
                _log.LogInformation(
                    "Log temizliği: {Toplam} satır silindi " +
                    "(denetim {Denetim}, e-posta {Eposta}, giris {Giris}, hata {Hata}).",
                    toplam, denetim, eposta, giris, hata);
            }
        }

    }
}
