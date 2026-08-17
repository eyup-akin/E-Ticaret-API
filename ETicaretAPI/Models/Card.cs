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

        // ⭐ YENİ — iyzico kart saklama jetonu. Tek tıkla ödeme bunsuz
        // yapılamaz; kart bilgisi bizde durmadığı için ödemeye giden
        // tek referans bu.
        //
        // ⚠️ null = bu satır iyzico'da yok (bu alandan önce elle
        // eklenmiş kartlar). Onlarla ödeme yapılamaz.
        public string? IyzicoCardToken { get; set; }

        // ⭐ YENİ — kartı veren banka ve BIN. iyzico'dan geliyor,
        // ekranda kartı ayırt etmek için.
        public string? BankaAdi { get; set; }
        public string? BinNumarasi { get; set; }
    }
}
