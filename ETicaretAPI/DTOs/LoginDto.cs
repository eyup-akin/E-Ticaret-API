using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // ⭐ DEĞİŞTİ — burada da hiç doğrulama yoktu.
    //
    // ⚠️ {"password":null} gönderen bir istek BCrypt.Verify(null, ...)
    // çağrısına düşüp 500 üretiyordu. 500'ler hem log'u kirletir hem de
    // saldırgana "burada doğrulama yok" sinyali verir.
    //
    // ⚠️ ŞİFRE KURALI (SifreGuclu) BURADA YOK — bilerek.
    //
    // Giriş, şifreyi BELİRLEMİYOR; var olanı doğruluyor. Kuralı burada
    // da uygulasaydık, kural sıkılaştırıldığı gün eski şifreli tüm
    // kullanıcılar sisteme giremez hale gelirdi. Şifre kuralı yalnızca
    // YAZMA yollarında (kayıt / sıfırlama / değiştirme) geçerlidir.
    public class LoginDto
    {
        [Required(ErrorMessage = "Email boş olamaz!")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Şifre boş olamaz!")]
        public string Password { get; set; } = string.Empty;
    }
}
