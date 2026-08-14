namespace ETicaretAPI.Models
{
    // ⭐ YENİ — E-POSTA GÖNDERİM KAYDI
    //
    // ⚠️ Bu tablo "GÖNDERDİK Mİ" sorusunu cevaplıyor, "ULAŞTI MI"
    // sorusunu değil. Teslimat/açılma bilgisi Brevo panelinde duruyor;
    // ikisini karıştırmak "gitti" yazan bir kaydı teslim sanmak olur.
    //
    // Bugüne kadar sonuç yalnızca ILogger'a düşüyordu: konteyner yeniden
    // başlayınca kayıt yok oluyor ve panelden hiç görünmüyordu.
    public class EmailKaydi
    {
        public int Id { get; set; }

        public string Alici { get; set; } = string.Empty;
        public string Konu { get; set; } = string.Empty;

        // Hangi bildirim: SiparisAlindi, SifreSifirlama, Iade:onaylandi…
        // Çağrı yerlerinde zaten var olan `olayAdi` parametresi.
        public string Olay { get; set; } = string.Empty;

        public bool Basarili { get; set; }

        // Yalnızca hata durumunda dolu.
        public string? HataMesaji { get; set; }

        // Brevo'nun döndürdüğü messageId — destek talebinde tek dayanak.
        // Konsol göndericisinde ve hatalarda null.
        public string? SaglayiciMesajId { get; set; }

        // ⚠️ GÖVDE YALNIZCA BAŞARISIZ KAYITLARDA SAKLANIYOR.
        //
        // Başarılıda saklamak sipariş içeriğini ikinci kez arşivlemek
        // olurdu. Ama "tekrar gönder" için elde bir içerik gerekiyor;
        // gitmeyen mailin gövdesi, gidene kadar duruyor ve başarılı
        // tekrar gönderimde siliniyor.
        public string? GovdeHtml { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
