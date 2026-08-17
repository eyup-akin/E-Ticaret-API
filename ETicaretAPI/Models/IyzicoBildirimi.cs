namespace ETicaretAPI.Models
{
    // ⭐ YENİ — iyzico webhook günlüğü.
    //
    // ⚠️ Aynı bildirim 3 kez gelebiliyor. Tekrarı if ile değil
    // IyziReferenceCode üzerindeki unique index ile eliyoruz
    // (kupon limitindeki desenin aynısı).
    public class IyzicoBildirimi
    {
        public int Id { get; set; }

        // iyzico'nun bildirim kimliği — unique.
        public string IyziReferenceCode { get; set; } = string.Empty;

        // ⚠️ Boş gelebiliyor; o zaman tekrar eleme Token+Durum'a düşer.
        public string? OlayTipi { get; set; }

        public string? Token { get; set; }
        public string? IyzicoPaymentId { get; set; }
        public string? Durum { get; set; }

        // HMAC doğrulaması geçti mi. ⚠️ Geçmese de kayıt tutuluyor:
        // "imzasız bildirim geldi" bilgisi silinmemeli.
        public bool ImzaGecerliMi { get; set; }

        public string? HamGovde { get; set; }

        public DateTime GelisZamani { get; set; } = DateTime.UtcNow;

        // İşleyici bunu gördü ve siparişe uyguladı mı.
        public bool IslendiMi { get; set; }
    }
}
