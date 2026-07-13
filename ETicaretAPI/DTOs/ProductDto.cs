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
    }
}