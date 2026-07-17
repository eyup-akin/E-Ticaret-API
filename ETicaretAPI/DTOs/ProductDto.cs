namespace ETicaretAPI.DTOs
{
    public class ProductDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public int CategoryId { get; set; }

        // ⭐ YENİ — barkod. Herkese açık (gizli bilgi değil).
        public string? Barcode { get; set; }

        // ⭐ YENİ — maliyet. SADECE admin isteklerinde dolar,
        // müşteri/misafir isteğinde null gider (controller'da hallediyoruz).
        public decimal? Cost { get; set; }

        // Listelerde tek resim yeter. Ana resim yoksa ilk resim, o da yoksa null.
        public string? MainImageUrl { get; set; }

        // Detay ekranında galeri için tüm resimler
        public List<ProductImageDto> Images { get; set; } = new List<ProductImageDto>();

        // Puan özeti (yorum yoksa ikisi de 0 kalır)
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }

        public int FavoriteCount { get; set; }
    }
}