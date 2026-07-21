using System.ComponentModel.DataAnnotations.Schema;

namespace ETicaretAPI.Models
{
    // ⭐ REFRESH TOKEN — uzun ömürlü "bana yeni access ver" bileti.
    //
    // NEDEN AYRI BİR TABLO?
    //   Access token (JWT) durumsuzdur; veritabanına yazılmaz, tek başına
    //   İPTAL EDİLEMEZ. Refresh token'ı ise burada saklıyoruz ki istediğimiz
    //   an geçersiz kılalım: cihaz çıkışı, hesap kilitleme, hırsızlık yakalama.
    //
    // NEDEN HAM TOKEN'I DEĞİL DE HASH'İNİ SAKLIYORUZ?
    //   Aynen şifre mantığı: veritabanı sızsa bile elindeki değerle giriş
    //   YAPILAMASIN. Kullanıcıya ham token'ı veririz; biz sadece onun
    //   SHA-256 hash'ini tutarız. Elimizdeki hash'ten ham token geri üretilemez.
    public class RefreshToken
    {
        public int Id { get; set; }

        // Bu token hangi kullanıcıya ait
        public int UserId { get; set; }

        // Ham token'ın SHA-256 hash'i (hex metin). Aramayı bunun üzerinden yaparız.
        public string TokenHash { get; set; } = string.Empty;

        // Ne zaman geçersiz olacak (üretim anı + 30 gün)
        public DateTime ExpiresAt { get; set; }

        // Ne zaman üretildi
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // İptal edildiyse zamanı tutar. null = HÂLÂ GEÇERLİ.
        // Rotation'da (yenisini verince) ve çıkışta bunu doldururuz.
        public DateTime? RevokedAt { get; set; }

        // Hangi cihazdan alındı (user-agent metni). "Bu cihazdan çıkış" için.
        public string? CihazBilgisi { get; set; }

        // ⭐ KOLAYLIK: token şu an kullanılabilir mi?
        // [NotMapped] = bu bir hesaplama; veritabanına KOLON olarak yazılmaz,
        // sadece C# tarafında iş görür. İptal edilmemiş VE süresi dolmamışsa aktiftir.
        [NotMapped]
        public bool Aktif => RevokedAt == null && ExpiresAt > DateTime.UtcNow;
    }
}