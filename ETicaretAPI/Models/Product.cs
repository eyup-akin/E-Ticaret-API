namespace ETicaretAPI.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }

        // Hangi kategoriye ait — Category tablosuna bağlanır
        public int CategoryId { get; set; }

        // Ürünün barkodu. Panelde/müşteride id yerine bunu göstereceğiz.
        // Nullable (string?): eski ürünlerde boş kalabilsin diye.
        // Yeni üründe zorunluluğu DTO tarafında kontrol edeceğiz.
        public string? Barcode { get; set; }

        // Bize maliyeti — kâr hesabı için. Müşteriye ASLA gönderilmez.
        // Nullable (decimal?): maliyeti girilmemiş eski ürünler için.
        public decimal? Cost { get; set; }
    }
}