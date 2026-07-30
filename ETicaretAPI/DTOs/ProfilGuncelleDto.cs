using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // Kullanıcının kendi profilini güncellerken gönderdiği paket.
    //
    // ⚠️ ŞU AN SADECE FullName VAR — Email BİLİNÇLİ OLARAK YOK.
    //
    //    Email değiştirmek göründüğünden çok daha karmaşık:
    //      1. Yeni adrese doğrulama maili gitmeli
    //      2. Doğrulanana kadar ESKİ adres geçerli kalmalı (yoksa kullanıcı
    //         hiçbir adrese erişemez hale gelir)
    //      3. "Bekleyen değişiklik" durumu için yeni kolonlar gerekir
    //      4. Yeni adres başkasında kayıtlıysa reddedilmeli — Users.Email
    //         üzerindeki benzersiz indekse takılır
    //      5. Değişince tüm oturumlar kapatılmalı
    //
    //    Bu tek başına ayrı bir aşama. Alan DTO'da olmadığı için istemci
    //    göndermeyi bile denemez.
    //
    //    Rol de burada YOK ve asla olmayacak — kullanıcı kendini admin
    //    yapamaz. Rol değişikliği yalnızca AdminController üzerinden,
    //    yetkili biri tarafından yapılır.
    public class ProfilGuncelleDto
    {
        [Required(ErrorMessage = "Ad soyad gerekli.")]
        [StringLength(100, MinimumLength = 2,
            ErrorMessage = "Ad soyad 2-100 karakter olmalı.")]
        public string FullName { get; set; } = string.Empty;
    }
}