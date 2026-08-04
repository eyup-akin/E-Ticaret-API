namespace ETicaretAPI.Models
{
    // ============================================================
    //  STOK HAREKET KAYDI (defter / ledger)
    //
    //  NE İŞE YARIYOR?
    //  Product.Stock bir BAKİYE — "şu an kaç adet var" der.
    //  Bu tablo ise DEFTER — "nasıl bu hale geldi" der.
    //
    //  Banka hesabı benzetmesi: bakiyeyi bilmek yetmez, ekstreyi
    //  de görmen gerekir. Yoksa "param nereye gitti" sorusunun
    //  cevabı olmaz.
    //
    //  NEDEN Stock KOLONUNU SİLİP HER SEFERİNDE TOPLAMIYORUZ?
    //  İki sebep:
    //    1) Her ürün listesi sorgusu bir SUM yapardı — 500 ürünlü
    //       sayfada 500 alt sorgu
    //    2) Atomik stok düşürmemiz çalışmazdı: koşullu UPDATE'in
    //       "WHERE Stock >= @adet" koşulu hesaplanan bir değere
    //       konulamaz — o koşul yarış koşulunu çözen şey
    //
    //  Yani Stock kolonu BİLİNÇLİ bir tekrardır.
    //
    //  ⚠️ TEKRARIN BEDELİ — DEĞİŞMEZ (INVARIANT):
    //     Product.Stock  ==  SUM(StockMovement.Miktar)
    //  her zaman doğru olmalı. Kontrol sorgusu yazılıp ara ara
    //  çalıştırılacak. Coupons.UsedCount ile CouponUsages
    //  arasındaki kontrolün aynısı.
    // ============================================================
    public class StockMovement
    {
        public int Id { get; set; }

        public int ProductId { get; set; }

        // Değişim miktarı — İŞARETLİ.
        //   negatif → stoktan düştü  (satış)
        //   pozitif → stoka eklendi  (iptal iadesi, manuel giriş)
        //
        // NEDEN AYRI BİR "Yon" KOLONU YOK?
        // Alternatif: Miktar hep pozitif + Yon ("giris"/"cikis").
        // İşaretli sayıyla toplam almak tek satır:
        //     SELECT SUM(Miktar) ...
        // Yön kolonu olsaydı her toplamada
        //     CASE WHEN Yon='cikis' THEN -Miktar ELSE Miktar END
        // yazmak gerekirdi — ve biri unutulduğunda hiçbir hata
        // çıkmadan yanlış sonuç verirdi.
        public int Miktar { get; set; }

        // Hareketten ÖNCEKİ ve SONRAKİ stok.
        //
        // Türetilebilir görünüyor (Sonraki = Onceki + Miktar).
        // Neden saklıyoruz?
        //
        // 1) BOŞLUK TESPİTİ: bir satırın SonrakiStok'u ile bir
        //    sonraki satırın OncekiStok'u tutmuyorsa, arada
        //    KAYDEDİLMEMİŞ bir değişiklik olmuş demektir. Bu, tam
        //    olarak korktuğumuz hatayı yakalar: "stok değişiyor
        //    ama kayıt yazılmıyor."
        //
        // 2) OKUNABİLİRLİK: ekranda "15 → 12" görmek, sadece
        //    "−3" görmekten çok daha anlaşılır.
        //
        // Bu bir DENETİM kaydı; denetim kayıtlarında fazlalık
        // israf değil, doğrulama aracıdır.
        public int OncekiStok { get; set; }
        public int SonrakiStok { get; set; }

        // Neden değişti?
        //   satis         → sipariş oluşturuldu
        //   iptal_iadesi  → sipariş iptal edildi, stok geri geldi
        //   manuel        → admin ürün formundan değiştirdi
        //   excel         → toplu içe aktarma
        //   iade          → iade onaylandı (Aşama 9)
        //
        // NEDEN string, NEDEN enum DEĞİL?
        // Projede durum alanları (Order.Status, Payment.Status)
        // zaten string. Tutarlılık okunabilirlikten önemli:
        // tek bir yerde enum kullanmak "acaba burada neden farklı"
        // sorusunu doğurur.
        //
        // Geçerli değerler StockMovementSebep sınıfında sabit
        // olarak duruyor — koda serpiştirilmiş sihirli metinler
        // yerine tek kaynak.
        public string Sebep { get; set; } = string.Empty;

        // Bu hareket neyden kaynaklandı?
        //   ReferansTipi: "Order" | "ImportJob" | null
        //   ReferansId  : o kaydın Id'si | null
        //
        // NEDEN GERÇEK BİR FOREIGN KEY DEĞİL?
        // Bu kolon bazen Order'a, bazen ImportJob'a işaret ediyor,
        // bazen hiçbir şeye (manuel düzenleme). Veritabanı böyle
        // bir ilişkiyi zorlayamaz — buna "polimorfik ilişki" denir.
        //
        // Alternatif her tip için ayrı nullable FK açmaktı:
        // OrderId?, ImportJobId?, ReturnRequestId?... Aşama 9'da
        // iade gelince bir kolon daha, sonra bir daha. Satırların
        // %90'ı boş kolon taşırdı.
        //
        // Bedeli: referans bütünlüğünü veritabanı değil kod
        // garanti ediyor. Denetim kaydı için kabul edilebilir —
        // bu tablodan JOIN atmıyoruz, sadece bilgi gösteriyoruz.
        public string? ReferansTipi { get; set; }
        public int? ReferansId { get; set; }

        // İşlemi yapan kullanıcı.
        //
        // Nullable çünkü her hareketin bir "yapan"ı yok:
        //   • Müşteri sipariş verdi   → müşterinin id'si
        //   • Admin elle düzeltti     → adminin id'si
        //   • Hangfire işi çalıştı    → null (sistem yaptı)
        public int? KullaniciId { get; set; }

        // Serbest açıklama — özellikle manuel düzeltmelerde.
        // "Depo sayımı sonrası düzeltme", "hasarlı ürün çıkışı"
        //
        // Nullable: otomatik hareketlerde (satış) yazılacak bir
        // şey yok, sebep zaten her şeyi anlatıyor.
        public string? Aciklama { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }


    // ============================================================
    //  SEBEP SABİTLERİ
    //
    //  NEDEN AYRI BİR SINIF?
    //  "satis" metnini beş ayrı dosyaya elle yazsaydık, birinde
    //  "satış" (Türkçe ş) veya "Satis" (büyük harf) yazmak
    //  hiçbir hata vermezdi — sadece o hareket filtrelerde
    //  görünmezdi. Sessiz hata.
    //
    //  Sabit kullanınca yazım hatası DERLEME hatası olur.
    //
    //  NEDEN const, NEDEN enum DEĞİL?
    //  Alan string olduğu için enum kullanmak her yazma ve okuma
    //  noktasında .ToString() / Enum.Parse çevirisi gerektirirdi.
    //  const string, aynı güvenliği sıfır çeviri maliyetiyle verir.
    // ============================================================
    public static class StokSebep
    {
        public const string Satis = "satis";
        public const string IptalIadesi = "iptal_iadesi";
        public const string Manuel = "manuel";
        public const string Excel = "excel";
        public const string Iade = "iade";
    }
}