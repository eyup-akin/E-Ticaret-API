namespace ETicaretAPI.Models
{
    public class Review
    {
        public int Id { get; set; }

        public int ProductId { get; set; }   // yorum yapılan ürün
        public int UserId { get; set; }       // yorumu yapan kullanıcı

        public int Rating { get; set; }        // 1-5 yıldız
        public string Comment { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
