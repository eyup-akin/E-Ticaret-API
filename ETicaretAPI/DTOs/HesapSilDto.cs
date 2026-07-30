using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // Kullanıcı hesabını kapatırken gönderdiği paket.
    //
    // Neden şifre soruyoruz?
    //   Hesap kapatma GERİ ALINAMAZ bir işlem. Biri telefonunu açık
    //   bıraktıysa yanına geçen kişi hesabını kapatabilmemeli.
    //
    //   Şifre değiştirmede de aynı korumayı koyduk (yeniden kimlik
    //   doğrulama). Hassas işlemlerde açık oturum yeterli değildir.
    //
    // Neden "onayMetni" gibi bir alan yok?
    //   "HESABIMI SİL yaz" tarzı onaylar ARAYÜZ kontrolüdür — kullanıcının
    //   ne yaptığını anlamasını sağlar. Sunucuya taşımanın güvenlik
    //   faydası yok, sadece boşa veri taşır. Çift onayı mobil tarafta
    //   yapacağız.
    public class HesapSilDto
    {
        [Required(ErrorMessage = "Hesabını kapatmak için şifren gerekli.")]
        public string Sifre { get; set; } = string.Empty;
    }
}