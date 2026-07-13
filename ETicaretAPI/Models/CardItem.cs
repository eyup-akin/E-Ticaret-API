namespace ETicaretAPI.Models
{
    public class CartItem
    {
        public int Id { get; set; }

        public int UserId { get; set; }     // Sepetin sahibi
        public int ProductId { get; set; }  // Sepetteki ürün
        public int Quantity { get; set; }   // Adet
    }
}