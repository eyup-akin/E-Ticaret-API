namespace ETicaretAPI.Models
{
    public class Favorite
    {
        public int Id { get; set; }

        public int UserId { get; set; }     // Favoriyi ekleyen
        public int ProductId { get; set; }  // Favorilenen ürün
    }
}