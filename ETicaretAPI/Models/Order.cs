namespace ETicaretAPI.Models
{
    public class Order
    {
        // Teknik anahtar — veritabanının iç kimliği, URL'lerde kullanılır
        public int Id { get; set; }

        // ⭐ YENİ — MÜŞTERİYE GÖSTERİLEN NUMARA
        // Format: SP-260724-4821
        // Id sıralı ve tahmin edilebilir olduğu için dışarı bunu veriyoruz.
        // Benzersizliğini AppDbContext'teki unique index garanti eder.
        public string OrderNumber { get; set; } = string.Empty;

        public int UserId { get; set; }
        public int AddressId { get; set; }
        public decimal Total { get; set; }
        public string Status { get; set; } = "hazirlaniyor"; // kargo durumu

        // Ödeme bilgileri
        public string PaymentStatus { get; set; } = "beklemede"; // beklemede / odendi / iade_edildi
        public string CardLast4 { get; set; } = string.Empty;

        // ⭐ KUPON — hepsi DONDURULMUŞ.
        // Kupon sonradan silinse/değiştirilse bile bu sipariş ne indirim
        // aldığını hatırlar. (UnitPrice ve Shipping* ile aynı mantık.)

        // İndirimden ÖNCEKİ tutar. Total = SubTotal - DiscountAmount.
        // Türetilebilir ama saklıyoruz: ileride kargo ücreti gibi kalemler
        // girerse formül bozulur, para hesabında risk almıyoruz.
        public decimal SubTotal { get; set; }

        // Kullanılan kupon kodu. Boş = kupon kullanılmadı.
        public string CouponCode { get; set; } = string.Empty;

        // Uygulanan indirim tutarı. 0 = indirim yok.
        public decimal DiscountAmount { get; set; }

        // kopyalanır ve bir daha değişmez.
        // (UnitPrice ve CardLast4'te uyguladığımız mantığın aynısı.)
        public string ShippingFullName { get; set; } = string.Empty;
        public string ShippingTitle { get; set; } = string.Empty;
        public string ShippingCity { get; set; } = string.Empty;
        public string ShippingFullAddress { get; set; } = string.Empty;

        // ⭐ YENİ — dondurulmuş alıcı telefonu.
        // Müşteri numarasını değiştirse bile bu sipariş hangi numarayla
        // gönderildiyse onu hatırlar.
        public string ShippingPhone { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // İptal bilgileri — nullable, iptal edilmemiş siparişlerde boş
        public string? CancelReason { get; set; }
        public DateTime? CancelledAt { get; set; }


        // Ayrıca burada saklanan metin bir DONDURULMUŞ değer gibi
        // davranıyor: firma listeden çıksa bile eski sipariş hangi
        // firmayla gittiğini hatırlıyor.
        public string? ShippingCompany { get; set; }

        // ve sayıya çevirirsek o sıfırlar kaybolur. Üzerinde toplama
        // çıkarma yapmadığımız her "numara" aslında bir METİNDİR.
        public string? TrackingNumber { get; set; }

      
        // tıklanan andır.
        public DateTime? ShippedAt { get; set; }

        // Teslim edildiği an (UTC).
        public DateTime? DeliveredAt { get; set; }

   
        // değiştirilip "ben böyle yazmıştım" tartışması çıkardı.
        public string? CustomerNote { get; set; }

    
        // durumu yok. Migration eski siparişlere 0 yazacak ve bu
        // doğru: o siparişlerde kargo gerçekten alınmamıştı.
        public decimal ShippingCost { get; set; } = 0;

        // ⭐ YENİ — DONDURULMUŞ KARGO KDV ORANI
        //
        // Kargo bir HİZMETTİR ve KDV'ye tabidir. ShippingCost da tıpkı
        // ürün fiyatları gibi KDV DAHİL bir tutar; oran onun üstüne
        // eklenmez, içinden ayrıştırılır.
        //
        // NEDEN AYRI BİR ALAN, NEDEN ÜRÜN ORANINI KULLANMIYORUZ?
        // Sepette %1'lik gıda ile %20'lik elektronik birlikte olabilir —
        // "siparişin KDV oranı" diye tek bir şey yok. Kargo kendi
        // hizmetidir ve kendi oranına tabidir. Kalemlerden birinin
        // oranını ödünç almak, sepetin içeriği değişince kargo KDV'sinin
        // de değişmesi gibi saçma bir sonuç doğururdu.
        //
        // Değeri MagazaAyarlari.KargoKdvOrani'ndan sipariş anında
        // kopyalanır. ShippingCost'un hemen yanında duruyor çünkü ikisi
        // birlikte anlam taşıyor: tutar ve o tutarın vergi oranı.
        //
        // ⚠️ nullable ve migration'da DOLDURULMUYOR — OrderItem.VatRate
        // ile aynı gerekçe. Eski siparişlerde hangi oranın uygulandığını
        // bilmiyoruz; zaten kargo da alınmamıştı. 0 yazmak "KDV'siz kargo
        // uygulandı" diye yanlış bir iddia olurdu.
        public int? ShippingVatRate { get; set; }

   
        // Benzersizliğini AppDbContext'teki (UserId, IdempotencyKey)
        // bileşik unique index garanti eder.
        public string? IdempotencyKey { get; set; }
    }
}