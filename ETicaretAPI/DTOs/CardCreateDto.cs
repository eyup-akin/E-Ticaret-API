using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // Kart eklerken kullanıcı bunları gönderir
    public class CardCreateDto
    {
        [Required(ErrorMessage = "Kart sahibi adı boş olamaz!")]
        public string CardHolderName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Kart numarası boş olamaz!")]
        [RegularExpression(@"^\d{16}$", ErrorMessage = "Kart numarası 16 haneli olmalı!")]
        public string CardNumber { get; set; } = string.Empty; // TAM numara — saklanmayacak

        [Range(1, 12, ErrorMessage = "Geçerli bir ay girin (1-12)!")]
        public int ExpiryMonth { get; set; }

        [Range(2024, 2100, ErrorMessage = "Geçerli bir yıl girin!")]
        public int ExpiryYear { get; set; }

        [Required(ErrorMessage = "CVV boş olamaz!")]
        [RegularExpression(@"^\d{3}$", ErrorMessage = "CVV 3 haneli olmalı!")]
        public string Cvv { get; set; } = string.Empty; // kontrol için alınır, ASLA saklanmaz
    }
}