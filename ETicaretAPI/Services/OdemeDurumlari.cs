namespace ETicaretAPI.Services
{
    // ⭐ YENİ — Order.PaymentStatus ve OdemeIslemi.Durum sabitleri.
    //
    // Metinler beş dosyada elle yazılıydı; değişince derleme hata
    // vermez, raporlar sessizce yanlış sayardı. SiparisDurumlari
    // aynı ihtiyaç için açılmıştı.
    public static class OdemeDurumlari
    {
        // ---- Order.PaymentStatus ----

        // Sipariş oluştu, ödeme henüz alınmadı. Stok rezerve.
        public const string OdemeBekliyor = "odeme_bekliyor";

        // ⚠️ iyzico fraudStatus = 0 → para kesin değil. Bunu "odendi"
        // saymak, ret gelirse ürünü kargoya vermiş olmak demek.
        public const string Incelemede = "odeme_incelemede";

        public const string Odendi = "odendi";
        public const string Basarisiz = "odeme_basarisiz";
        public const string IadeEdildi = "iade_edildi";
        public const string KismiIade = "kismi_iade";

        // Bu alandan önceki siparişlerde yazan değer.
        public const string Beklemede = "beklemede";

        // ---- OdemeIslemi.Durum ----

        public const string DenemeBaslatildi = "baslatildi";
        public const string DenemeBasarili = "basarili";
        public const string DenemeBasarisiz = "basarisiz";
        public const string DenemeSuresiDoldu = "suresi_doldu";

        // ⚠️ Ciro sorgularının dışlaması gereken durumlar. Ödenmemiş
        // sipariş satış değildir; unutulursa ciro şişer.
        public static readonly string[] OdenmemisSayilanlar =
        {
            OdemeBekliyor, Basarisiz, Beklemede
        };
    }
}
