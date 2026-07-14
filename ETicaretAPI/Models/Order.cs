namespace ETicaretAPI.Models
{
    public class Order
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int AddressId { get; set; }
        public decimal Total { get; set; }
        public string Status { get; set; } = "hazirlaniyor"; // kargo durumu

        // Ödeme bilgileri
        public string PaymentStatus { get; set; } = "beklemede"; // beklemede / odendi / iade_edildi
        public string CardLast4 { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ⭐ YENİ — iptal bilgileri
        // Nullable (?) çünkü iptal edilmemiş siparişlerde boş olacak.
        public string? CancelReason { get; set; }
        public DateTime? CancelledAt { get; set; }
    }
}