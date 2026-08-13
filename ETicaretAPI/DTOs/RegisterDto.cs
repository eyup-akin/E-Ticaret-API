using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // ⭐ DEĞİŞTİ — bu DTO'da HİÇ doğrulama yoktu.
    //
    // ⚠️ Sisteme giriş kapısı olan DTO, projedeki 39 DTO'nun tamamından
    // farklı olarak tek bir öznitelik taşımıyordu. Sonuçları:
    //
    //   {"password":"1"}   → tek karakterli şifreyle hesap açılıyordu
    //   {"email":"asdf"}   → doğrulama maili hiçbir yere gitmiyor,
    //                        hesap KALICI ÖLÜ kalıyordu (giriş yapamaz,
    //                        yeniden kayıt olamaz — e-posta dolu)
    //   {"email":null}     → dto.Email.Trim() → NullReferenceException
    //                        → 500 (kullanıcıya "sunucu hatası")
    //
    // Doğrulama SUNUCUDA yapılıyor; mobilin form kontrolü yeterli değil,
    // çünkü istek Postman'den veya betikten de gelebilir.
    public class RegisterDto
    {
        [Required(ErrorMessage = "Ad soyad boş olamaz!")]
        [StringLength(100, MinimumLength = 2,
            ErrorMessage = "Ad soyad 2-100 karakter olmalı!")]
        public string FullName { get; set; } = string.Empty;

        // ⚠️ 254: RFC 5321'in e-posta üst sınırı. AppDbContext'teki
        // HasMaxLength(256) ile uyumlu — DTO biraz daha dar olmalı ki
        // veritabanı istisnası yerine anlaşılır bir mesaj dönsün.
        [Required(ErrorMessage = "Email boş olamaz!")]
        [EmailAddress(ErrorMessage = "Geçerli bir email adresi gir!")]
        [StringLength(254, ErrorMessage = "Email adresi çok uzun!")]
        public string Email { get; set; } = string.Empty;

        // ⚠️ Uzunluk kuralı burada YAZILI DEĞİL, SifreGucluAttribute'ta.
        // Aynı kural şifre sıfırlama ve şifre değiştirmede de geçerli;
        // üç yere ayrı ayrı yazmak onları ayrıştırırdı.
        [Required(ErrorMessage = "Şifre boş olamaz!")]
        [SifreGuclu]
        public string Password { get; set; } = string.Empty;

        // ⭐ YENİ (Aşama 10) — gizlilik politikası + kullanım koşulları onayı.
        //
        // ⚠️ Varsayılan false ve sunucu true olmasını ŞART koşuyor:
        // "onay göndermeyen istek onaylamış sayılır" demek, açık rızayı
        // ortadan kaldırırdı.
        //
        // ⚠️ Öznitelikle değil, controller'da kontrol ediliyor — çünkü
        // reddedilme mesajı bir doğrulama hatasından çok bir iş kuralı
        // ("Kayıt için ... onaylaman gerekiyor").
        public bool SozlesmeOnayi { get; set; }
    }
}
