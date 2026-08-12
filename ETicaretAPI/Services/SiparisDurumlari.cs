namespace ETicaretAPI.Services
{
    // ⭐ YENİ (7.1) — SİPARİŞ DURUMLARININ TEK KAYNAĞI
    //
    // ⚠️ BU SINIF NEDEN AÇILDI?
    // `"iptal"` metni elle yazılmış halde ÜÇ dosyada geçiyordu:
    // `OrdersController` durum makinesi, `ReportsController`in geçerli
    // sipariş süzgeci ve `ProductsController`in popülerlik sıralaması.
    // Üçü de aynı şeyi söylüyordu — "iptal sayılmaz" — ama ortak bir
    // sabitleri yoktu. Ana sayfa bölümleri ("şu an revaçta" ve
    // "sana özel") dördüncü ve beşinci tüketici oldu; toplamayı daha
    // fazla ertelemenin anlamı kalmadı.
    //
    // Tehlike şuydu: durum adı bir gün değişirse (örneğin
    // "iptal" → "iptal_edildi") derleme HATA VERMEZ, sadece o rapor ve
    // o sıralama sessizce yanlış sayardı. Sabitle artık değişiklik
    // tek yerden yapılıyor ve yazım hatası derleme hatasına dönüşüyor.
    //
    // ⚠️ NEDEN enum DEĞİL?
    // `Order.Status` alanı zaten `string` ve veritabanında da string
    // duruyor. Enum'a çevirmek her okuma ve yazmada çeviri maliyeti
    // getirir, EF sorgularında `Where(o => o.Status != ...)` ifadesini
    // SQL'e çevirmeyi zorlaştırır ve migration gerektirirdi. Kazanç
    // yalnızca "yazım hatası yakalama"ydı — onu `const string` de
    // veriyor.
    //
    // ⚠️ Bu sınıf durum GEÇİŞLERİNİ tanımlamıyor. Hangi durumdan
    // hangisine geçilebileceği `OrdersController.GecerliGecisler`
    // sözlüğünde; orası bir iş kuralı, burası bir sözlük.
    public static class SiparisDurumlari
    {
        public const string Hazirlaniyor = "hazirlaniyor";
        public const string Kargoda = "kargoda";
        public const string TeslimEdildi = "teslim_edildi";
        public const string Iptal = "iptal";
    }
}
