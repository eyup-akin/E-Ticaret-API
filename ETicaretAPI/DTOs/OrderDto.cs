namespace ETicaretAPI.DTOs
{
    public class OrderDto
    {
        public int Id { get; set; }
        public decimal Total { get; set; }
        public string Status { get; set; } = string.Empty;       // kargo durumu
        public string PaymentStatus { get; set; } = string.Empty; // ödeme durumu
        public string CardLast4 { get; set; } = string.Empty;
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