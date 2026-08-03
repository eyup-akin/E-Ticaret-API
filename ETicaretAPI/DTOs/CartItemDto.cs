namespace ETicaretAPI.DTOs
{
    public class CartItemDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal ProductPrice { get; set; }
        public int Quantity { get; set; }

        public string? ProductImageUrl { get; set; }    // ⭐ ana resim


        // ⭐ YENİ — bu ürün hâlâ satışta mı?
        //
        // Sepette bekleyen ürün, beklerken pasife düşmüş olabilir.
        // Mobil taraf bunu görüp satırı gri gösterecek ve "Siparişi
        // Tamamla"yı engelleyecek. Bu alan olmasaydı müşteri butona basar,
        // sunucudan anlaşılmaz bir hata alır ve neyin yanlış olduğunu
        // bilemezdi.
        public bool IsActive { get; set; }

    }
}