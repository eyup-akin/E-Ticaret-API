namespace ETicaretAPI.Models
{
    public class OrderItem
    {
        public int Id { get; set; }

        public int OrderId { get; set; }    // Bağlı olduğu sipariş
        public int ProductId { get; set; }  // Sipariş edilen ürün
        public int Quantity { get; set; }   // Adet

        // Sipariş anındaki fiyat — sonradan ürün fiyatı değişse bile bu sabit kalır
        public decimal UnitPrice { get; set; }
    }
}