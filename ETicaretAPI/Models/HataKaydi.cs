namespace ETicaretAPI.Models
{
    // ⭐ YENİ — SUNUCU HATASI (500) KAYDI
    //
    // HataYakalamaMiddleware bugüne kadar yalnızca ILogger'a yazıyordu;
    // konteyner yeniden başlayınca hata izi kayboluyordu.
    //
    // ⚠️ Bu kayıt tetikleyen isteğin transaction'ına YAZILAMAZ — o
    // transaction geri alınıyor. Kendi kapsamında yazılıyor.
    public class HataKaydi
    {
        public int Id { get; set; }

        public string Yol { get; set; } = string.Empty;
        public string Yontem { get; set; } = string.Empty;

        public string Mesaj { get; set; } = string.Empty;

        // ⚠️ Yığın izi tabloya giriyor (bilinçli karar): ekran yalnızca
        // süperadmine açık ve izsiz bir hata kaydı "bir şey patladı"
        // demekten öteye gitmiyor — yani konteyner günlüğüne bakma
        // zorunluluğunu kaldırmıyor, ki tabloyu tutmanın amacı buydu.
        public string? YiginIzi { get; set; }

        // Token okunabildiyse dolu; kimliksiz isteklerde null.
        public int? KullaniciId { get; set; }

        public string? IpAdresi { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
