namespace ETicaretAPI.Models
{
    public class Card
    {
        public int Id { get; set; }

        public int UserId { get; set; } // kartın sahibi

        public string CardHolderName { get; set; } = string.Empty; // kart üzerindeki isim
        public string Last4Digits { get; set; } = string.Empty;    // SADECE son 4 hane
        public string CardType { get; set; } = string.Empty;       // Visa / Mastercard

        public int ExpiryMonth { get; set; } // son kullanma ayı
        public int ExpiryYear { get; set; }  // son kullanma yılı
    }
}