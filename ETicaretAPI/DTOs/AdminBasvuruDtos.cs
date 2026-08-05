using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // Başvuru formu — herkese açık uçtan gelir.
    public class AdminBasvuruCreateDto
    {
        [Required(ErrorMessage = "E-posta zorunlu!")]
        [EmailAddress(ErrorMessage = "Geçerli bir e-posta gir!")]
        public string Email { get; set; } = string.Empty;

        // ⚠️ ŞİFRE NEDEN İSTENİYOR?
        //
        // Sadece e-posta alsaydık, herkes başkasının adına başvuru
        // açabilirdi. Süperadmin ekranında "Ahmet admin olmak
        // istiyor" yazardı ama Ahmet'in haberi olmazdı.
        //
        // Şifre, başvuranın o hesabın SAHİBİ olduğunun kanıtı.
        // "Hassas işlemde yeniden kimlik doğrulama" kuralı —
        // hesap kapatmada da aynısını yapıyoruz.
        [Required(ErrorMessage = "Şifre zorunlu!")]
        public string Sifre { get; set; } = string.Empty;

        [Required(ErrorMessage = "Gerekçe yazman gerekiyor!")]
        [StringLength(1000, MinimumLength = 20,
            ErrorMessage = "Gerekçe 20-1000 karakter olmalı!")]
        public string Gerekce { get; set; } = string.Empty;
    }


    // Reddetme gerekçesi — süperadmin gönderir.
    public class BasvuruRedDto
    {
        [Required(ErrorMessage = "Red nedeni yazmalısın!")]
        [StringLength(500, MinimumLength = 5,
            ErrorMessage = "Red nedeni 5-500 karakter olmalı!")]
        public string RedNedeni { get; set; } = string.Empty;
    }
}