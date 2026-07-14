using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    public class StatusUpdateDto
    {
        [Required(ErrorMessage = "Durum boş olamaz!")]
        public string Status { get; set; } = string.Empty;
    }
}