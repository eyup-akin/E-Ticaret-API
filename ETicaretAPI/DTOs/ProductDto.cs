namespace ETicaretAPI.DTOs
{
    public class ProductDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public int CategoryId { get; set; }

        // ⭐ YENİ — resimler

        // Listelerde tek resim yeter (ürün kartı). Ana resim yoksa ilk resim.
        // Hiç resim yoksa null.
        public string? MainImageUrl { get; set; }

        // Detay ekranında galeri için tüm resimler
        public List<ProductImageDto> Images { get; set; } = new List<ProductImageDto>();


        // ⭐ YENİ — puan özeti (yorum yoksa ikisi de 0 kalır → kartta yıldız görünmez)
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }

        public int FavoriteCount { get; set; }   // ⭐ YENİ — kaç kişinin favorisinde

    }
}