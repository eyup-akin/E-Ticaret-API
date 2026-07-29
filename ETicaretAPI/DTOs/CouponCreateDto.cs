using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // Admin YENİ kupon oluştururken gönderdiği paket.
    //
    // ⚠️ Burada OLMAYAN alanlar bilinçli olarak yok:
    //   Id              → veritabanı üretir
    //   UsedCount       → sistem sayar, admin elle dolduramaz
    //   CreatedAt       → sunucu damgalar
    //   CreatedByUserId → token'dan okunur, istekten değil
    //
    // Bunları DTO'ya koysaydık, istekte gönderilen değer kabul edilirdi.
    // Buna "over-posting" denir ve klasik bir güvenlik açığıdır.
    public class CouponCreateDto
    {
        // Müşterinin sepette yazacağı kod.
        // Kaydederken Trim().ToUpperInvariant() ile normalize edilecek —
        // KuponServisi doğrularken de aynı normalizasyonu yapıyor.
        // İkisi aynı olmazsa "kupon var ama bulunamıyor" hatası çıkar.
        [Required(ErrorMessage = "Kupon kodu zorunlu!")]
        [StringLength(50, MinimumLength = 3,
            ErrorMessage = "Kupon kodu 3-50 karakter olmalı!")]
        public string Code { get; set; } = string.Empty;

        // Sadece admin görür. "Yılbaşı kampanyası" gibi.
        [Required(ErrorMessage = "Açıklama zorunlu!")]
        [StringLength(200, MinimumLength = 2,
            ErrorMessage = "Açıklama 2-200 karakter olmalı!")]
        public string Description { get; set; } = string.Empty;

        // "yuzde" veya "tutar" — başka değer kabul edilmez.
        // Bu kontrolü attribute ile değil controller'da yapıyoruz,
        // çünkü kabul edilen değerler listesi ileride büyüyebilir
        // ve tek bir yerde durması gerekiyor.
        [Required(ErrorMessage = "İndirim tipi zorunlu!")]
        public string DiscountType { get; set; } = "yuzde";

        // yuzde ise 10 = %10
        // tutar ise 50 = 50 TL
        // Üst sınırı burada geniş tutuyoruz; asıl kontrol (yüzde ise 0-100)
        // controller'daki iş kuralı doğrulamasında yapılıyor.
        [Range(0.01, 1000000,
            ErrorMessage = "İndirim değeri 0'dan büyük olmalı!")]
        public decimal DiscountValue { get; set; }

        // 0 = alt sınır yok. Negatif olamaz.
        [Range(0, 1000000,
            ErrorMessage = "Minimum sepet tutarı negatif olamaz!")]
        public decimal MinOrderAmount { get; set; }

        // Yüzdeli kuponlarda indirim tavanı. null = tavan yok.
        // Tutar tipinde anlamsızdır; controller null'a çevirir.
        [Range(0.01, 1000000,
            ErrorMessage = "İndirim tavanı 0'dan büyük olmalı!")]
        public decimal? MaxDiscountAmount { get; set; }

        [Required(ErrorMessage = "Başlangıç tarihi zorunlu!")]
        public DateTime StartsAt { get; set; }

        [Required(ErrorMessage = "Bitiş tarihi zorunlu!")]
        public DateTime EndsAt { get; set; }

        // Toplam kaç kez kullanılabilir. null = sınırsız.
        [Range(1, int.MaxValue,
            ErrorMessage = "Toplam kullanım limiti en az 1 olmalı!")]
        public int? UsageLimit { get; set; }

        // Bir kullanıcı kaç kez kullanabilir. Genelde 1.
        [Range(1, 100,
            ErrorMessage = "Kişi başı limit 1-100 arası olmalı!")]
        public int UsageLimitPerUser { get; set; } = 1;

        // null = tüm ürünlerde geçerli.
        // Dolu ise sadece o kategorideki ürünlerin toplamına indirim uygulanır.
        public int? CategoryId { get; set; }

        // Kupon hemen yayına girsin mi, taslak olarak mı dursun?
        public bool IsActive { get; set; } = true;
    }
}