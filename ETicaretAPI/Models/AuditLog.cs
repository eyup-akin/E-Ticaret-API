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

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}