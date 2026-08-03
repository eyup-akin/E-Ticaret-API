using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // Admin'in sipariş durumunu değiştirirken gönderdiği veri.
    public class StatusUpdateDto
    {
        [Required(ErrorMessage = "Durum boş olamaz!")]
        public string Status { get; set; } = string.Empty;

        // ⭐ YENİ — KARGO BİLGİLERİ
        //
        // Sadece Status "kargoda" yapılırken doldurulur; diğer
        // geçişlerde null gelir.
        //
        // ⚠️ Neden [Required] koymadık, madem "kargoda" için zorunlu?
        //
        // Çünkü [Required] KOŞULSUZ çalışır — "teslim_edildi" veya
        // "iptal_edildi" geçişinde de takip numarası isterdi ki bu saçma.
        //
        // Veri doğrulama öznitelikleri (attribute) tek bir alana bakar,
        // alanlar ARASI kuralı ifade edemez. "A doluysa B de dolu olmalı"
        // türü kurallar İŞ MANTIĞINDA yazılır — bizim durumumuzda
        // AdminController'daki durum güncelleme metodunda (1.2'de).
        //
        // Bu genel bir prensip:
        //   • Alan bazlı kural (boş olamaz, en fazla 50 karakter,
        //     0'dan büyük olmalı)          → öznitelik
        //   • Alanlar arası / duruma bağlı kural → iş mantığı
        [MaxLength(50, ErrorMessage = "Kargo firması adı en fazla 50 karakter olabilir!")]
        public string? ShippingCompany { get; set; }

        [MaxLength(50, ErrorMessage = "Takip numarası en fazla 50 karakter olabilir!")]
        public string? TrackingNumber { get; set; }
    }
}