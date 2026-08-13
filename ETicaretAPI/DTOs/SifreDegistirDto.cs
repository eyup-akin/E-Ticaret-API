using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // Giriş yapmış kullanıcı şifresini değiştirirken gönderdiği paket.
    //
    // ⚠️ Burada "email" veya "userId" YOK — bilinçli.
    //    Kimin şifresini değiştirdiğimizi TOKEN'dan okuyoruz.
    //    Eğer DTO'da userId olsaydı, biri başkasının id'sini gönderip
    //    onun şifresini değiştirmeyi denerdi. Kimlik bilgisi asla
    //    istekten alınmaz, her zaman token'dan okunur.
    public class SifreDegistirDto
    {
        // Neden eski şifre soruyoruz?
        //
        //   Tehdit modeli: Biri telefonunu masada açık bıraktı. Yanına
        //   geçen kişi Hesabım → Şifre Değiştir'e girip yeni şifre
        //   belirlerse hesabı TAMAMEN ele geçirir — sen bile giremezsin.
        //
        //   Eski şifreyi sormak bu saldırıyı imkânsız kılar: elinde açık
        //   oturum olması yetmez, şifreyi de bilmesi gerekir.
        //
        //   Buna "yeniden kimlik doğrulama" (re-authentication) denir ve
        //   hassas işlemlerin standart korumasıdır.
        [Required(ErrorMessage = "Mevcut şifren gerekli.")]
        public string EskiSifre { get; set; } = string.Empty;

        // ⭐ DEĞİŞTİ — [MinLength(6)] yerine ortak öznitelik.
        //
        // Kural burada, kayıtta ve şifre sıfırlamada AYNI olmak zorunda;
        // "sıfırlarken kabul edilen şifre değiştirirken reddediliyor"
        // gibi bir tuhaflık çıkmasın diye. Elle yazılı üç kopya yerine
        // tek kaynak: SifreGucluAttribute.
        [Required(ErrorMessage = "Yeni şifre gerekli.")]
        [SifreGuclu]
        public string YeniSifre { get; set; } = string.Empty;
    }
}