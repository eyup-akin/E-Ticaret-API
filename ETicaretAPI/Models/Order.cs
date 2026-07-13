namespace ETicaretAPI.Models
{
    public class Order
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int AddressId { get; set; }
        public decimal Total { get; set; }
        public string Status { get; set; } = "hazirlaniyor"; // kargo durumu

        // YENİ — ödeme bilgileri
        public string PaymentStatus { get; set; } = "beklemede"; // beklemede / odendi
        public string CardLast4 { get; set; } = string.Empty;    // ödemede kullanılan kart (dondurulur)


        // ⭐ YENİ — sipariş ne zaman verildi
        // Varsayılan değer sayesinde OrdersController'da ekstra kod yazmaya gerek yok:
        // new Order { ... } dediğin anda otomatik "şu an" yazılır.
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    }
}