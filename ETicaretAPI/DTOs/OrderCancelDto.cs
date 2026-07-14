using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    public class OrderCancelDto
    {
        [Required(ErrorMessage = "İptal sebebi zorunludur!")]
        [StringLength(500, MinimumLength = 5,
            ErrorMessage = "İptal sebebi 5-500 karakter olmalı!")]
        public string Reason { get; set; } = string.Empty;
    }
}