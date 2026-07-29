using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // Admin VAR OLAN kuponu düzenlerken gönderdiği paket.
    //
    // ⚠️ CouponCreateDto'dan tek farkı: BURADA "Code" YOK.
    //
    // Neden yok?
    //   Kupon kodu değiştirilemez, çünkü geçmiş siparişler o koda
    //   referans veriyor. Order.CouponCode alanı DONDURULMUŞ bir metin —
    //   foreign key değil. "INDIRIM10" kodunu "YAZ25" yapsaydık,
    //   eski siparişlerde yazan "INDIRIM10" hiçbir kupona denk gelmezdi
    //   ve Aşama 8'deki kupon raporu bozulurdu.
    //
    //   Alternatif: alanı burada tutup sunucuda yok saymak.
    //   Onu seçmedik çünkü client "kodu gönderdim, değişmedi" durumuna
    //   düşerdi. Alan hiç yoksa yanlış anlaşılma da olmaz.
    //
    // UsedCount da yok — o sistemin saydığı bir sayaç, admin elle
    // oynayamaz. Oynayabilseydi kullanım limiti anlamını yitirirdi.
    public class CouponUpdateDto
    {
        [Required(ErrorMessage = "Açıklama zorunlu!")]
        [StringLength(200, MinimumLength = 2,
            ErrorMessage = "Açıklama 2-200 karakter olmalı!")]
        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "İndirim tipi zorunlu!")]
        public string DiscountType { get; set; } = "yuzde";

        [Range(0.01, 1000000,
            ErrorMessage = "İndirim değeri 0'dan büyük olmalı!")]
        public decimal DiscountValue { get; set; }

        [Range(0, 1000000,
            ErrorMessage = "Minimum sepet tutarı negatif olamaz!")]
        public decimal MinOrderAmount { get; set; }

        [Range(0.01, 1000000,
            ErrorMessage = "İndirim tavanı 0'dan büyük olmalı!")]
        public decimal? MaxDiscountAmount { get; set; }

        [Required(ErrorMessage = "Başlangıç tarihi zorunlu!")]
        public DateTime StartsAt { get; set; }

        [Required(ErrorMessage = "Bitiş tarihi zorunlu!")]
        public DateTime EndsAt { get; set; }

        [Range(1, int.MaxValue,
            ErrorMessage = "Toplam kullanım limiti en az 1 olmalı!")]
        public int? UsageLimit { get; set; }

        [Range(1, 100,
            ErrorMessage = "Kişi başı limit 1-100 arası olmalı!")]
        public int UsageLimitPerUser { get; set; } = 1;

        public int? CategoryId { get; set; }

        public bool IsActive { get; set; } = true;
    }
}