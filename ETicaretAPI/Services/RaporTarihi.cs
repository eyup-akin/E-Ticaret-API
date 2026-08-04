namespace ETicaretAPI.Services
{
    // ============================================================
    //  BİR RAPORUN KAPSADIĞI TARİH ARALIĞI
    //
    //  Dört alan taşıyor çünkü iki farklı tüketicisi var:
    //    • UTC alanları  → veritabanı sorgusuna girer
    //    • Yerel alanlar → cevapta ekrana geri gönderilir
    //      ("hangi aralığı gösteriyorum" bilgisi kullanıcıya lazım)
    //
    //  record kullanıyoruz: üretildikten sonra değişmemesi gereken
    //  küçük bir veri paketi. Yanlışlıkla değiştirmek derleme hatası
    //  verir, çalışma zamanında sürpriz olmaz.
    // ============================================================
    public record RaporAraligi(
        // Sorguda: >= bu değer
        DateTime BaslangicUtc,

        // Sorguda: < bu değer  (DİKKAT: dahil değil, hariç)
        //
        // Neden "23:59:59" değil de ertesi günün başlangıcı?
        // 23:59:59.9999999 anında yazılmış bir satır "<= 23:59:59"
        // koşuluna takılmaz ve sessizce raporun dışında kalır.
        // Yarı açık aralık [başlangıç, bitiş) bu deliği kapatır.
        DateTime BitisUtcHaric,

        // Ekrana geri yazmak için — kullanıcının seçtiği günler
        DateTime BaslangicYerel,
        DateTime BitisYerel);


    // ============================================================
    //  RAPOR TARİHİ HESAPLAYICI
    //
    //  NEDEN AYRI BİR SINIF?
    //  Dokuz rapor endpoint'i de aynı tarih mantığını kullanacak.
    //  Her birine kopyalasaydık, saat dilimi kuralı değiştiğinde
    //  dokuz yere dokunmak gerekirdi ve biri mutlaka unutulurdu.
    //
    //  "Tek doğru kaynak" ilkesi: aynı gerçek iki yerde yaşamaz.
    //
    //  NEDEN static DEĞİL?
    //  Saat dilimi appsettings'ten geliyor; static sınıf
    //  IConfiguration alamaz. EmailSablonlari'nda da aynı sebeple
    //  normal sınıf kullanmıştık.
    // ============================================================
    public class RaporTarihi
    {
        // Saat dilimi bilgisi. readonly: kurucuda bir kez atanır,
        // sonra kimse değiştiremez.
        private readonly TimeZoneInfo _dilim;

        // Aralık verilmezse kaç günlük varsayılan gösterilecek.
        //
        // Neden 30? Bir aylık dönem hem yeterince veri içerir hem
        // ekranda okunabilir kalır. 7 gün fazla dar (haftalık dalga
        // bile görünmez), 90 gün grafiği okunmaz hale getirir.
        private const int VarsayilanGunSayisi = 30;

        public RaporTarihi(IConfiguration config)
        {
            // appsettings'ten saat dilimi adını oku.
            var dilimAdi = config["Uygulama:SaatDilimi"];

            // ⚠️ NEDEN try/catch?
            //
            // FindSystemTimeZoneById, bilinmeyen bir ID gelirse
            // TimeZoneTimeNotFoundException fırlatır. Bu hata
            // UYGULAMA AÇILIRKEN değil, İLK RAPOR İSTEĞİNDE patlardı
            // ve sebebi anlaşılmazdı ("raporlar çalışmıyor").
            //
            // Ayrıca sunucu Linux'a taşınırsa ID farklı olabilir.
            // Rapor gösterememektense bir saat kaymış rapor
            // göstermek daha iyi — ama sessizce değil, log ile.
            try
            {
                _dilim = TimeZoneInfo.FindSystemTimeZoneById(
                    string.IsNullOrWhiteSpace(dilimAdi)
                        ? "Turkey Standard Time"
                        : dilimAdi);
            }
            catch (TimeZoneNotFoundException)
            {
                // Son çare: sabit UTC+3.
                //
                // CreateCustomTimeZone gerçek bir saat dilimi nesnesi
                // üretir; böylece kodun geri kalanı "acaba gerçek mi
                // yedek mi" diye sormak zorunda kalmaz. Aynı arayüz.
                _dilim = TimeZoneInfo.CreateCustomTimeZone(
                    id: "TR-Yedek",
                    baseUtcOffset: TimeSpan.FromHours(3),
                    displayName: "Türkiye (yedek)",
                    standardDisplayName: "Türkiye (yedek)");
            }
        }


        // ------------------------------------------------------------
        //  UTC bir tarihi yerel saate çevirir.
        //
        //  Nerede kullanılır: veriyi çektikten SONRA, günlere
        //  bölerken. Sorgunun içinde kullanılamaz — EF Core bu
        //  metodu SQL'e çeviremez, "could not be translated" hatası
        //  verir. Bu bilinçli bir kısıt: filtreleme SQL'de,
        //  gruplama bellekte.
        // ------------------------------------------------------------
        public DateTime YereleCevir(DateTime utcTarih)
        {
            // ⚠️ SpecifyKind neden gerekli?
            //
            // EF Core veritabanından DateTime okurken Kind alanını
            // "Unspecified" yapar (datetime2 kolonu saat dilimi
            // taşımaz). ConvertTimeFromUtc ise Kind'ı "Local" olan
            // bir değer verilirse hata fırlatır.
            //
            // SpecifyKind değeri DEĞİŞTİRMEZ, sadece etiketini
            // düzeltir: "bu sayı zaten UTC, öyle davran."
            var utc = DateTime.SpecifyKind(utcTarih, DateTimeKind.Utc);

            return TimeZoneInfo.ConvertTimeFromUtc(utc, _dilim);
        }


        // ------------------------------------------------------------
        //  Yerel bir tarihi UTC'ye çevirir.
        //
        //  Nerede kullanılır: kullanıcının seçtiği gün sınırlarını
        //  sorguya sokulabilir hale getirirken.
        // ------------------------------------------------------------
        public DateTime UtcyeCevir(DateTime yerelTarih)
        {
            var yerel = DateTime.SpecifyKind(yerelTarih, DateTimeKind.Unspecified);

            return TimeZoneInfo.ConvertTimeToUtc(yerel, _dilim);
        }


        // ------------------------------------------------------------
        //  ⭐ ASIL İŞ: sorgudan gelen iki tarihi rapor aralığına çevirir.
        //
        //  Tüm rapor endpoint'leri ilk satırda bunu çağıracak.
        //
        //  Girdi:  ?baslangic=2026-08-01&bitis=2026-08-31
        //  Çıktı:  UTC aralığı + gösterim için yerel günler
        // ------------------------------------------------------------
        public RaporAraligi Aralik(DateTime? baslangic, DateTime? bitis)
        {
            // "Bugün" nedir? UTC'ye göre değil, YEREL saate göre.
            // Türkiye'de 5 Ağustos 01:00 iken UTC 4 Ağustos'tur;
            // varsayılanı UTC'den hesaplasaydık kullanıcı bir gün
            // eksik veri görürdü.
            var bugunYerel = YereleCevir(DateTime.UtcNow).Date;

            // Bitiş verilmemişse bugün.
            // .Date → saat kısmını atar, sadece gün kalır. Kullanıcı
            // "31 Ağustos" derken saat kastetmiyor.
            var bitisGunu = (bitis ?? bugunYerel).Date;

            // Başlangıç verilmemişse son 30 gün.
            //
            // AddDays(-29) neden -30 değil?
            // Bugün dahil 30 gün istiyoruz. Bugün 1 gün sayılır,
            // geriye 29 gün eklenir. -30 yazsaydık 31 günlük bir
            // aralık çıkardı — klasik "çit direği" hatası.
            var baslangicGunu = (baslangic ?? bitisGunu.AddDays(-(VarsayilanGunSayisi - 1))).Date;

            // ⚠️ Kullanıcı tarihleri ters girerse (bitiş < başlangıç)
            // sorgu boş döner ve kimse sebebini anlamaz. Sessizce
            // düzeltiyoruz — hata vermek yerine makul davranmak,
            // kullanıcının hiçbir şey yapamadığı bir durumda daha
            // iyi bir deneyim.
            if (bitisGunu < baslangicGunu)
            {
                (baslangicGunu, bitisGunu) = (bitisGunu, baslangicGunu);
            }

            // ⭐ ASIL ÇEVİRİM
            //
            // Başlangıç: seçilen günün YEREL 00:00'ı, UTC karşılığı
            //   1 Ağustos 00:00 TR  →  31 Temmuz 21:00 UTC
            var baslangicUtc = UtcyeCevir(baslangicGunu);

            // Bitiş: seçilen günün ERTESİ gününün yerel 00:00'ı.
            //   31 Ağustos'u dahil etmek istiyoruz, o yüzden sınır
            //   1 Eylül 00:00 TR  →  31 Ağustos 21:00 UTC
            //   ve sorguda "<" kullanılıyor.
            var bitisUtcHaric = UtcyeCevir(bitisGunu.AddDays(1));

            return new RaporAraligi(
                baslangicUtc,
                bitisUtcHaric,
                baslangicGunu,
                bitisGunu);
        }
    }
}