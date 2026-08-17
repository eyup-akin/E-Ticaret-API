namespace ETicaretAPI.Models
{
    // ⭐ YENİ — ödemenin sepet kalemi kırılımı.
    //
    // iyzico iadeyi kalem bazında yapıyor: her kalemin kendi
    // paymentTransactionId'si var ve kısmi iade bunsuz imkânsız.
    // Ödeme anında kaydedilmezse sonradan üretilemez.
    public class OdemeKalemi
    {
        public int Id { get; set; }

        public int OdemeIslemiId { get; set; }

        // ⚠️ null = KARGO satırı (ürün değil). Kargoyu da saklıyoruz,
        // yoksa tam iadede kargo tutarı iyzico'ya hiç gitmez ve
        // müşteriye eksik para döner.
        public int? OrderItemId { get; set; }

        // ⚠️ İade isteğinin zorunlu alanı.
        public string IyzicoPaymentTransactionId { get; set; } = string.Empty;

        public decimal Price { get; set; }
        public decimal PaidPrice { get; set; }

        // Birden çok kısmi iade olabilir; birikimli takip ediliyor.
        public decimal IadeEdilenTutar { get; set; } = 0;
    }
}
