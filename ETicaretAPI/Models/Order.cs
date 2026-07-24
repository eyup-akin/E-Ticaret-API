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

        // ⭐ YENİ — DONDURULMUŞ TESLİMAT ADRESİ
        // AddressId hâlâ duruyor ama artık ona GÜVENMİYORUZ.
        // Müşteri adresini sonradan düzenlerse/silerse eski siparişin
        // kargo etiketi yanlış çıkardı. Sipariş anındaki hali buraya
        // kopyalanır ve bir daha değişmez.
        // (UnitPrice ve CardLast4'te uyguladığımız mantığın aynısı.)
        public string ShippingFullName { get; set; } = string.Empty;
        public string ShippingTitle { get; set; } = string.Empty;
        public string ShippingCity { get; set; } = string.Empty;
        public string ShippingFullAddress { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // İptal bilgileri — nullable, iptal edilmemiş siparişlerde boş
        public string? CancelReason { get; set; }
        public DateTime? CancelledAt { get; set; }
    }
}