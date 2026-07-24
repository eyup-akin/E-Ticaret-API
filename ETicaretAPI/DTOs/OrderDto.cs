namespace ETicaretAPI.DTOs
{
    public class OrderDto
    {
        public int Id { get; set; }                              // teknik anahtar (URL için)
        public string OrderNumber { get; set; } = string.Empty;   // ⭐ ekranda gösterilen numara

        public decimal Total { get; set; }
        public string Status { get; set; } = string.Empty;       // kargo durumu
        public string PaymentStatus { get; set; } = string.Empty; // ödeme durumu
        public string CardLast4 { get; set; } = string.Empty;

        // ⭐ Dondurulmuş teslimat adresi — sipariş anındaki hali
        public string ShippingFullName { get; set; } = string.Empty;
        public string ShippingTitle { get; set; } = string.Empty;
        public string ShippingCity { get; set; } = string.Empty;
        public string ShippingFullAddress { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }        // sipariş tarihi
        public string? CancelReason { get; set; }      // iptal sebebi (null = iptal değil)
        public DateTime? CancelledAt { get; set; }     // iptal tarihi

        public List<OrderItemDto> Items { get; set; } = new();
    }

    // Sipariş içindeki her ürün satırı
    public class OrderItemDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; } // dondurulmuş fiyat
    }
}