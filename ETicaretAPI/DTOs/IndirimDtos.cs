using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // ⭐ YENİ — ÜRÜNE İNDİRİM UYGULAMA (admin → sunucu)
    public class IndirimUygulaDto
    {
        // "yuzde" | "tutar"
        //
        // ⚠️ Kupon tipleriyle (DiscountType) AYNI kelimeler ama AYRI
        // kavram: kupon sepete, bu ürünün etiketine uygulanıyor.
        // Ortak bir enum'a bağlasaydık kupona bir tip eklendiğinde
        // burası da sessizce onu kabul etmiş görünürdü.
        [Required(ErrorMessage = "İndirim tipi zorunlu.")]
        public string Tip { get; set; } = "yuzde";

        // yuzde → 1-90 arası tam sayı gibi kullanılır
        // tutar → indirilecek TL
        [Range(0.01, 1000000, ErrorMessage = "İndirim değeri sıfırdan büyük olmalı.")]
        public decimal Deger { get; set; }
    }

    // Toplu indirim: aynı oranı birden çok ürüne uygular.
    public class TopluIndirimDto
    {
        [Required]
        [MinLength(1, ErrorMessage = "En az bir ürün seçilmeli.")]
        public List<int> UrunIdleri { get; set; } = new();

        [Required(ErrorMessage = "İndirim tipi zorunlu.")]
        public string Tip { get; set; } = "yuzde";

        [Range(0.01, 1000000, ErrorMessage = "İndirim değeri sıfırdan büyük olmalı.")]
        public decimal Deger { get; set; }
    }
}
