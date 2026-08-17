namespace ETicaretAPI.DTOs
{
    // Dışarıya kart gönderirken — SADECE güvenli alanlar.
    // ⚠️ IyzicoCardToken buraya GİRMEZ: jeton ödeme yetkisi taşıyor.
    public class CardDto
    {
        public int Id { get; set; }
        public string CardHolderName { get; set; } = string.Empty;
        public string Last4Digits { get; set; } = string.Empty; // "**** 1234" göstermek için
        public string CardType { get; set; } = string.Empty;
        public int ExpiryMonth { get; set; }
        public int ExpiryYear { get; set; }

        // ⭐ YENİ — kartı veren banka (iyzico'dan geliyor, olmayabilir).
        public string? BankaAdi { get; set; }

        // ⭐ YENİ — iyzico jetonu var mı. false ise bu kart eski
        // kayıttan geliyor ve ödemede kullanılamaz.
        public bool OdemeyeHazir { get; set; }
    }
}
