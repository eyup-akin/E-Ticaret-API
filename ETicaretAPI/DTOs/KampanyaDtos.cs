using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // ⭐ YENİ (B2) — KAMPANYA KAYDETME (admin → sunucu)
    //
    // ⚠️ Kupon kodları ve koşullar burada DİZİ, veritabanında satır
    // satır tek kolon. Dönüşüm controller'da tek yerde yapılıyor:
    // panelin "her satır bir madde" kutusuyla, saklama biçimini
    // birbirine bağlamamak için.
    public class KampanyaKaydetDto
    {
        [Required(ErrorMessage = "Başlık zorunlu.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Başlık 2-100 karakter olmalı.")]
        public string Baslik { get; set; } = string.Empty;

        [Required(ErrorMessage = "Kısa açıklama zorunlu.")]
        [StringLength(200, MinimumLength = 2, ErrorMessage = "Kısa açıklama 2-200 karakter olmalı.")]
        public string KisaAciklama { get; set; } = string.Empty;

        [Required(ErrorMessage = "Süre metni zorunlu.")]
        [StringLength(100, ErrorMessage = "Süre metni en fazla 100 karakter olabilir.")]
        public string BitisMetni { get; set; } = string.Empty;

        [Required(ErrorMessage = "Açıklama zorunlu.")]
        [StringLength(2000, MinimumLength = 10, ErrorMessage = "Açıklama 10-2000 karakter olmalı.")]
        public string Aciklama { get; set; } = string.Empty;

        // ⚠️ Görsel ZORUNLU. Görselsiz bir afiş şeritte boş bir kutu
        // olurdu; "kampanya var ama gösterilecek bir şey yok" diye bir
        // durum yaratmıyoruz.
        [Required(ErrorMessage = "Görsel zorunlu.")]
        [StringLength(300)]
        public string GorselUrl { get; set; } = string.Empty;

        public List<string> KuponKodlari { get; set; } = new();
        public List<string> Kosullar { get; set; } = new();

        [Range(0, 999, ErrorMessage = "Sıra 0-999 arasında olmalı.")]
        public int Sira { get; set; }

        public bool AktifMi { get; set; } = true;
    }
}
