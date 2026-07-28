namespace ETicaretAPI.Models
{
    // Bir kuponun bir kullanıcı tarafından bir siparişte kullanıldığı kaydı.
    //
    // Ne işe yarar:
    //   1) "Kişi başı 1 kez" kuralını uygulamak
    //   2) Aşama 6'da "bu kupon ne kadar ciro getirdi" raporu
    //   3) Denetim: kim ne zaman ne kadar indirim aldı
    public class CouponUsage
    {
        public int Id { get; set; }

        public int CouponId { get; set; }
        public int UserId { get; set; }
        public int OrderId { get; set; }

        // O siparişte uygulanan gerçek indirim tutarı.
        // Kuponun tanımından hesaplanabilirdi ama kupon sonradan
        // değişebilir — dondurulmuş kayıt tutuyoruz.
        public decimal DiscountAmount { get; set; }

        public DateTime UsedAt { get; set; } = DateTime.UtcNow;
    }
}