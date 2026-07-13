namespace ETicaretAPI.Models
{
    public class ProductImage
    {
        public int Id { get; set; }

        public int ProductId { get; set; }   // hangi ürünün resmi

        // Diskteki dosyanın web yolu — örn: /uploads/urunler/a3f9c1.jpg
        // Tam adres DEĞİL. Tam adresi (http://...:5289) istemci ekler.
        // Böylece sunucu adresi değişince veritabanına dokunmaya gerek kalmaz.
        public string Url { get; set; } = string.Empty;

        public bool IsMain { get; set; } = false;  // ana resim (listede/kartta gösterilecek)

        public int SortOrder { get; set; } = 0;    // galeri sırası
    }
}