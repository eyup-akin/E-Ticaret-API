namespace ETicaretAPI.Models
{
    // ⭐ YENİ — bir ödeme denemesi. Kart reddedilip tekrar denenirse
    // ikinci satır oluşur.
    //
    // ⚠️ Payment tablosuna yazılmıyor: orası para hareketi defteri ve
    // IadeHesaplayici onu okuyor. Başarısız deneme para hareketi değil.
    public class OdemeIslemi
    {
        public int Id { get; set; }

        public int OrderId { get; set; }
        public int UserId { get; set; }

        // Bizim ürettiğimiz eşleştirme anahtarı; cevapta aynısı geri gelir.
        public string ConversationId { get; set; } = string.Empty;

        // Checkout Form token'ı — sonucu bununla sorguluyoruz.
        public string Token { get; set; } = string.Empty;

        // Token ~30 dk yaşıyor; süre aşımı işi buna bakıyor.
        public DateTime? TokenGecerlilik { get; set; }

        // OdemeDurumlari.Deneme* sabitleri
        public string Durum { get; set; } = string.Empty;

        // ⚠️ İade ve iptal bunsuz yapılamaz.
        public string? IyzicoPaymentId { get; set; }

        public decimal Price { get; set; }

        // ⚠️ Taksitte Price'tan büyük olur, farkı müşteri öder. Sipariş
        // Total'i değişmez — ikisini karıştırmak ciroyu şişirir.
        public decimal? PaidPrice { get; set; }

        public int Taksit { get; set; } = 1;
        public string ParaBirimi { get; set; } = "TRY";

        // ⚠️ 1 = onaylı, 0 = iyzico incelemede (para kesin değil), -1 = ret.
        public int? FraudDurumu { get; set; }

        // 3DS sonucu; 1 = başarılı.
        public int? MdStatus { get; set; }

        public string? HataKodu { get; set; }
        public string? HataMesaji { get; set; }

        // iyzico'dan gelen tek kart bilgisi — PAN hiç gelmiyor.
        public string? KartTipi { get; set; }
        public string? KartAilesi { get; set; }
        public string? BinNumarasi { get; set; }
        public string? Son4Hane { get; set; }

        // Ham cevap: admin panelde "iyzico ne dedi" sorusunun cevabı.
        // Alanlara ayırdığımız kısım her zaman eksik kalıyor.
        public string? HamCevap { get; set; }

        public DateTime OlusturmaZamani { get; set; } = DateTime.UtcNow;

        // Sonucun öğrenildiği an; null ise deneme yarım kalmış.
        public DateTime? TamamlanmaZamani { get; set; }
    }
}
