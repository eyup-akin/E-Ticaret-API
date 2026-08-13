using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // "Yeni şifre belirle" — maildeki token + yeni şifre.
    //
    // ⭐ DEĞİŞTİ — doğrulama controller'daki elle if'lerden buraya taşındı.
    // Uzunluk kuralı artık SifreGucluAttribute'ta; kayıt ve şifre
    // değiştirme de aynı özniteliği kullanıyor.
    public class SifreYenileDto
    {
        [Required(ErrorMessage = "Sıfırlama linki geçersiz.")]
        public string Token { get; set; } = string.Empty;

        [Required(ErrorMessage = "Yeni şifre gerekli.")]
        [SifreGuclu]
        public string YeniSifre { get; set; } = string.Empty;
    }
}
