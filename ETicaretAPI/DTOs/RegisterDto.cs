using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    public class RegisterDto
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        // ⭐ YENİ (Aşama 10) — gizlilik politikası + kullanım koşulları onayı.
        //
        // ⚠️ Varsayılan false ve sunucu true olmasını ŞART koşuyor:
        // "onay göndermeyen istek onaylamış sayılır" demek, açık rızayı
        // ortadan kaldırırdı.
        public bool SozlesmeOnayi { get; set; }
    }
}
