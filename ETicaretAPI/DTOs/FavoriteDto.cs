namespace ETicaretAPI.DTOs
{
    public class FavoriteDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal ProductPrice { get; set; }

        // ⭐ DEĞİŞTİ — ham stok KALDIRILDI.
        //
        // ⚠️ Bu uç TAMAMEN müşteriye ait ([Authorize], admin dalı yok).
        // Yani buradaki `Stock` alanı her zaman gerçek stok sayısını
        // müşteriye gönderiyordu — ProductDto'da kapattığımız sızıntının
        // aynısı, burada kapatılmamıştı.
        //
        // Yerine ProductDto ile AYNI türetilmiş alanlar geliyor ki
        // favori listesindeki kart ile ana sayfadaki kart aynı bilgiyi
        // aynı biçimde alsın — ikisi de aynı UrunKarti bileşenini
        // besliyor.
        public string StokDurumu { get; set; } = "var";
        public int? KalanAdet { get; set; }

        public string? ProductImageUrl { get; set; }    // ⭐ ana resim (yoksa null)

    }
}