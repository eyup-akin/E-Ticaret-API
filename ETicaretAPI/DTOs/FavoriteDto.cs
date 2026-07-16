namespace ETicaretAPI.DTOs
{
    public class FavoriteDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal ProductPrice { get; set; }

        public int Stock { get; set; }                 // ⭐ karttaki stok rozeti için
        public string? ProductImageUrl { get; set; }    // ⭐ ana resim (yoksa null)

    }
}