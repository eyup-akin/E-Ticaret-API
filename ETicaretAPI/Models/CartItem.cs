namespace ETicaretAPI.Models
{
    public class CartItem
    {
        public int Id { get; set; }

        public int UserId { get; set; }     // Sepetin sahibi
        public int ProductId { get; set; }  // Sepetteki ürün
        public int Quantity { get; set; }   // Adet

        // ⭐ YENİ — ÜRÜN SEPETE EKLENDİĞİ ANDAKİ FİYAT
        //
        // ⚠️ BU BİR DONDURMA DEĞİL, BİR TANIK.
        //
        // Sipariş kalemindeki UnitPrice dondurulmuş fiyattır:
        // "müşteri bu tutarı ödedi" der ve bağlayıcıdır.
        //
        // Buradaki değer bağlayıcı DEĞİL. Tek işi şu soruyu
        // cevaplamak: "müşteri bu ürünü sepete atarken gördüğü
        // fiyat neydi?" Bugünkü fiyatla farklıysa müşteriye
        // söylüyoruz.
        //
        // ⚠️ SİPARİŞ HER HÂLÜKÂRDA GÜNCEL FİYATTAN OLUŞUR.
        //
        // Bu alanı sipariş anında kullanmıyoruz. Kullansaydık,
        // sepette bekleyen bir ürünü eski fiyattan satmış olurduk —
        // mağaza zarar ederdi ve fiyat artışları hiçbir zaman
        // yürürlüğe girmezdi. Dondurma SİPARİŞ anında olur, sepete
        // eklemede değil.
        //
        // Nullable: bu alan eklenmeden önceki sepet satırlarında
        // boş kalıyor. Boş olanlarda "fiyat değişti mi?" sorusu
        // sorulamıyor ve uyarı gösterilmiyor — 0 yazsaydık her eski
        // satır "fiyat düştü" diye işaretlenirdi.
        public decimal? EklenmeFiyati { get; set; }
    }
}