using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    public class KombinKaydetDto
    {
        [Required(ErrorMessage = "Kombin adı boş olamaz!")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Kombin adı 2-100 karakter olmalı!")]
        public string Ad { get; set; } = string.Empty;

        [StringLength(300, ErrorMessage = "Açıklama en fazla 300 karakter!")]
        public string? Aciklama { get; set; }

        // ⚠️ Üst sınır 50: daha fazlası muhtemelen yazım hatası ve
        // kombin fiyatını maliyetin altına düşürebilir.
        [Range(0, 50, ErrorMessage = "İndirim yüzdesi 0-50 arasında olmalı!")]
        public int IndirimYuzdesi { get; set; }

        public bool AktifMi { get; set; } = true;

        // Ürün sayısı kontrolü controller'da (en az 2, en fazla 5).
        public List<int> UrunIdleri { get; set; } = new();
    }
}
