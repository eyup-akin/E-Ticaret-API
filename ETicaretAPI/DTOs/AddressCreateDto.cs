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
    }
}