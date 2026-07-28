using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    public class AddressCreateDto
    {
        [Required(ErrorMessage = "Adres başlığı boş olamaz!")]
        [StringLength(50, ErrorMessage = "Başlık en fazla 50 karakter!")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Açık adres boş olamaz!")]
        public string FullAddress { get; set; } = string.Empty;

        [Required(ErrorMessage = "Şehir boş olamaz!")]
        public string City { get; set; } = string.Empty;

        // ⭐ YENİ — telefon zorunlu.
        // Regex açıklaması: başta isteğe bağlı +, sonra 10-15 arası
        // rakam/boşluk/tire/parantez. Ülke koduyla veya kodsuz yazılabilir.
        // Katı bir format dayatmıyoruz — kullanıcıyı yormanın anlamı yok,
        // amaç "burası telefon değil" durumunu yakalamak.
        [Required(ErrorMessage = "Telefon numarası boş olamaz!")]
        [RegularExpression(@"^\+?[0-9\s\-\(\)]{10,20}$",
            ErrorMessage = "Geçerli bir telefon numarası gir (örn: 0532 123 45 67)")]
        public string Phone { get; set; } = string.Empty;   
    }
}