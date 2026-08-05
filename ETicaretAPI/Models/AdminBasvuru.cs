namespace ETicaretAPI.Models
{
    // ============================================================
    //  ADMİN BAŞVURUSU
    //
    //  NEDEN AYRI BİR TABLO, NEDEN User'A BİR KOLON DEĞİL?
    //
    //  User'a "BasvuruDurumu" diye bir kolon eklesek şunları
    //  kaybederdik:
    //    • Başvuru GEÇMİŞİ — kişi bir kez reddedilip sonra kabul
    //      edilirse, ilk reddin sebebi ve tarihi kaybolurdu
    //    • Gerekçe metni her kullanıcı satırında yer kaplardı
    //      (kullanıcıların %99'u hiç başvurmayacak)
    //    • "Kim karar verdi" bilgisi User'a ait değil, başvuruya ait
    //
    //  Kural: bir OLAYIN kaydı, olayın öznesine kolon olarak
    //  eklenmez — kendi tablosunu hak eder.
    // ============================================================
    public class AdminBasvuru
    {
        public int Id { get; set; }

        // Başvuran kullanıcı. Kayıt zaten var (customer olarak),
        // biz sadece ona işaret ediyoruz.
        public int UserId { get; set; }

        // "Neden admin olmak istiyorsun?" — süperadmin kararını
        // buna bakarak verecek.
        public string Gerekce { get; set; } = string.Empty;

        // beklemede / onaylandi / reddedildi
        //
        // Neden string, neden enum değil? Projedeki tüm durum
        // alanları (Order.Status, Payment.Status, StockMovement.Sebep)
        // string. Tutarlılık, tek bir yerde enum kullanmanın
        // sağlayacağı tip güvenliğinden değerli — çünkü "acaba
        // burada neden farklı" sorusu her okuyanı durdurur.
        public string Durum { get; set; } = BasvuruDurumu.Beklemede;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ---- KARAR BİLGİLERİ ----
        //
        // Üçü de nullable: başvuru "beklemede" iken henüz karar yok.
        // Bu, "değer yok" durumunun doğru temsili.

        // Kararı veren süperadmin. Denetim için kritik: "bu kişiyi
        // admin yapan kimdi?" sorusunun cevabı.
        public int? KararVerenUserId { get; set; }

        public DateTime? KararTarihi { get; set; }

        // Sadece reddedilen başvurularda dolar.
        public string? RedNedeni { get; set; }
    }


    // ============================================================
    //  DURUM SABİTLERİ
    //
    //  "beklemede" metnini beş dosyaya elle yazsaydık, birinde
    //  "Beklemede" (büyük harf) yazmak hiçbir hata vermezdi —
    //  sadece o başvuru filtrelerde görünmezdi. Sessiz hata.
    //  Sabit kullanınca yazım hatası DERLEME hatası olur.
    //
    //  StokSebep sınıfındaki desenin aynısı.
    // ============================================================
    public static class BasvuruDurumu
    {
        public const string Beklemede = "beklemede";
        public const string Onaylandi = "onaylandi";
        public const string Reddedildi = "reddedildi";
    }
}