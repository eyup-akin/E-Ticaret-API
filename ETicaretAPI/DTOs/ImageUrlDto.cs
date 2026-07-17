using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    public class ImageUrlDto
    {
        [Required(ErrorMessage = "Resim URL'si zorunlu!")]
        [Url(ErrorMessage = "Geçerli bir URL girin!")]
        public string Url { get; set; } = string.Empty;
    }
}