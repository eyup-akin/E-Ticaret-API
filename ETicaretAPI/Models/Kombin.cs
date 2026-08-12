namespace ETicaretAPI.Models
{
    // ⭐ YENİ — ÜRÜN KOMBİNİ ("birlikte iyi gider")
    //
    // Admin elle tanımlıyor ve gerçek bir indirimi var. Tanımlı kombin
    // yoksa ürün detayı sipariş verisinden otomatik öneri üretiyor
    // (o önerilerin indirimi YOK).
    public class Kombin
    {
        public int Id { get; set; }

        public string Ad { get; set; } = string.Empty;

        public string? Aciklama { get; set; }

        // ⚠️ Yüzde, sabit tutar değil: fiyat değişince tasarruf da
        // kendiliğinden ölçekleniyor. Sabit tutar yazsaydık ürün
        // zamlandığında indirim erir, ucuzladığında toplamı eksiye
        // düşürebilirdi.
        public int IndirimYuzdesi { get; set; }

        public bool AktifMi { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }


    public class KombinUrun
    {
        public int Id { get; set; }

        public int KombinId { get; set; }
        public int ProductId { get; set; }
    }
}
