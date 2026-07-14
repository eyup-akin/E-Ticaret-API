namespace ETicaretAPI.DTOs
{
    public class CategoryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        // ⭐ YENİ — bu kategoride kaç ürün var
        // Admin panelde göstereceğiz; mobil tarafta kullanılmıyorsa da zararı yok.
        public int ProductCount { get; set; }
    }
}