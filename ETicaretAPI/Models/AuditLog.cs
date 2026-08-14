namespace ETicaretAPI.Models
{
    // DENETİM İZİ (audit trail)
    // "Kim, kimi, ne zaman, hangi role aldı?"
    // Yetki değişiklikleri sistemin en kritik olaylarıdır — hepsi kaydedilir.
    // Gerçek şirketlerde bu tablo YASAL zorunluluktur.
    public class AuditLog
    {
        public int Id { get; set; }

        // İŞLEMİ YAPAN
        public int ActorUserId { get; set; }
        public string ActorName { get; set; } = string.Empty;

        // İŞLEM YAPILAN
        public int TargetUserId { get; set; }
        public string TargetName { get; set; } = string.Empty;

        // rol_degisti | pasiflestirildi | aktiflestirildi
        public string Action { get; set; } = string.Empty;

        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        // ⭐ YENİ — işlemin geldiği istemci adresi.
        //
        // ⚠️ NULLABLE ve öyle kalmalı: Hangfire işlerinin ve sistem
        // tetiklemelerinin isteği yok. Boş bırakmak "bilinmiyor" der,
        // 0.0.0.0 yazmak yalan söylerdi. (StockMovement.KullaniciId ile
        // aynı gerekçe.) Eski kayıtlar da null kalıyor — uydurulmuş bir
        // IP, olmayan bir kanıt üretir.
        public string? IpAdresi { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}