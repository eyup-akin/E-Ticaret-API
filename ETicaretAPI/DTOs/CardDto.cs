namespace ETicaretAPI.DTOs
{
    // Dışarıya kart gönderirken — SADECE güvenli alanlar
    public class CardDto
    {
        public int Id { get; set; }
        public string CardHolderName { get; set; } = string.Empty;
        public string Last4Digits { get; set; } = string.Empty; // "**** 1234" göstermek için
        public string CardType { get; set; } = string.Empty;
        public int ExpiryMonth { get; set; }
        public int ExpiryYear { get; set; }
    }
}