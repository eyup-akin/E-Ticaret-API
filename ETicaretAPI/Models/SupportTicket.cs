namespace ETicaretAPI.Models
{
    // ============================================================
    //  ⭐ YENİ (Aşama 8) — DESTEK TALEBİ
    //
    //  ⚠️ NEDEN İKİ TABLO (talep + mesaj), NEDEN TEK TABLO DEĞİL?
    //
    //  Tek tabloyla başlamak cazip: "konu + mesaj + cevap" üç kolon,
    //  bitti. Ama müşteri cevaba cevap yazmak İSTEYECEK — bu
    //  kaçınılmaz. O gün tek tabloyu bölmek, veriyi taşımak ve tüm
    //  uçları yeniden yazmak demekti. `Orders` / `OrderItems`
    //  ayrımının mantığı birebir aynı: bir başlık, altında n satır.
    //
    //  ⚠️ NEDEN Order'A KOLON DEĞİL?
    //  Talebin siparişle ilgisi OLMAYABİLİR ("kargo ne zaman gelir"
    //  ile "hesabımı açamıyorum" aynı sistemde). `OrderId` bu yüzden
    //  nullable bir BAĞLANTI, zorunlu bir sahip değil.
    // ============================================================
    public class SupportTicket
    {
        public int Id { get; set; }

        // Talebi açan müşteri.
        public int UserId { get; set; }

        // ⚠️ NULLABLE — talep bir siparişe bağlı OLMAYABİLİR.
        // "Şifremi değiştiremiyorum" diyen müşterinin siparişi yok.
        // Zorunlu yapsaydık böyle bir talep hiç açılamazdı.
        //
        // Doluysa admin tek tıkla siparişe gidebiliyor ve müşteriye
        // "hangi sipariş?" diye sormak zorunda kalmıyor.
        public int? OrderId { get; set; }

        public string Konu { get; set; } = string.Empty;

        // kargo / urun / odeme / diger
        //
        // ⚠️ Beyaz liste SUNUCUDA doğrulanıyor. Mobilin açılır menü
        // göstermesi yetmez — istek Postman'den de gelebilir.
        public string Kategori { get; set; } = DestekKategorisi.Diger;

        // acik / yanitlandi / kapali
        public string Durum { get; set; } = DestekDurumu.Acik;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ⚠️ SON HAREKET ZAMANI — her mesajda tazeleniyor.
        //
        // Admin listesi buna göre sıralanıyor: "en son konuşulan
        // üstte". `CreatedAt`'e göre sıralasaydık üç hafta önce
        // açılmış ama dün cevap yazılmış bir talep listenin dibinde
        // kalırdı.
        //
        // ⚠️ Türetilebilir bir değer (son mesajın tarihi) ama
        // saklanıyor: alternatifi her liste sorgusunda mesaj
        // tablosuna korelasyonlu alt sorgu atmaktı. `Product.Stock`
        // kolonunun defter varken korunma gerekçesinin aynısı.
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Talebi kapatan kişi. Müşteri de kapatabilir, admin de.
        //
        // ⚠️ Nullable: açık talepte henüz kapatan yok. "Değer yok"
        // durumunun doğru temsili — 0 yazmak "0 numaralı kullanıcı
        // kapattı" demek olurdu.
        public int? KapatanUserId { get; set; }
    }


    // ⚠️ Enum değil `const string`: alan zaten string ve
    // veritabanında da string duruyor. Projedeki bütün durum
    // alanları (Order.Status, AdminBasvuru.Durum, StockMovement.Sebep)
    // aynı deseni kullanıyor.
    public static class DestekDurumu
    {
        // Müşteri yazdı, sıra BİZDE.
        public const string Acik = "acik";

        // Admin cevapladı, sıra MÜŞTERİDE.
        public const string Yanitlandi = "yanitlandi";

        // İş bitti.
        public const string Kapali = "kapali";

        // ⚠️⚠️ PLANDAKİ DÖRDÜNCÜ DURUM ("musteri_bekleniyor")
        // YAZILMADI — bilinçli.
        //
        // Yol haritası dört durum sayıyordu: acik / yanitlandi /
        // musteri_bekleniyor / kapali. Ama "yanitlandi" ile
        // "musteri_bekleniyor" AYNI GERÇEĞİ anlatıyor: admin
        // konuştu, sıra müşteride. İkisi birden dursaydı kodun
        // hangisini yazacağına karar vermesi gerekirdi ve o karar
        // keyfi olurdu; iki ayrı durum aynı şeyi gösterince de
        // admin listesindeki filtreler bölünür, "yanıtlandı"
        // sekmesinde bazı talepler eksik görünürdü.
        //
        // Adminin gerçekten ihtiyacı olan ayrım tek: "bana bakıyor
        // mu, bakmıyor mu?" Onu `acik` veriyor.
    }


    public static class DestekKategorisi
    {
        public const string Kargo = "kargo";
        public const string Urun = "urun";
        public const string Odeme = "odeme";
        public const string Diger = "diger";

        // Doğrulama tek yerden okusun diye burada.
        public static readonly string[] Gecerliler =
        {
            Kargo, Urun, Odeme, Diger
        };
    }
}
