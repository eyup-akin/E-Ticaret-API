using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // ⭐ YENİ — SÖZLEŞME METNİ GÜNCELLEME
    //
    // ⚠️ GÖVDEDE ÜÇ AYRI KAPI VAR VE ÜÇÜ DE BİLEREK.
    // Metin dışındaki iki alan "yanlışlıkla" yapılmasını
    // zorlaştırmak için: yasal metin, ekranda yanlış hizalanmış bir
    // buton yüzünden değişebilecek bir şey değil.
    public class SozlesmeGuncelleDto
    {
        // ⚠️ Alt sınır 50 karakter: boş ya da tek satırlık bir metin
        // yayına çıkarsa mağaza yasal olarak metinsiz kalır ve bunu
        // kimse fark etmez. Üst sınır bellek koruması.
        [Required(ErrorMessage = "Sözleşme metni boş olamaz.")]
        [MinLength(50, ErrorMessage = "Sözleşme metni en az 50 karakter olmalı.")]
        [MaxLength(100000, ErrorMessage = "Sözleşme metni çok uzun.")]
        public string Icerik { get; set; } = string.Empty;

        // ⚠️ ŞİFRE TEKRAR SORULUYOR ("sudo" deseni).
        //
        // Token'ı olan herkes süperadmindir demek yetmiyor: açık
        // bırakılmış bir panel ya da çalınmış bir oturum, yasal metni
        // değiştirebilir hale gelirdi. Hesap kapatmada da aynı desen
        // var — geri alınamayan işler kimliği yeniden sorar.
        [Required(ErrorMessage = "Şifreni girmen gerekiyor.")]
        public string Sifre { get; set; } = string.Empty;

        // ⚠️ ELLE YAZILAN ONAY. Ekrandaki kutuya "ONAYLIYORUM"
        // yazılmadan istek geçmiyor.
        //
        // Neden sunucu da kontrol ediyor? Ön yüzdeki kontrol sadece
        // KAZAYI önler; isteği elle atan biri onu hiç görmez.
        // "Yalnızca backend kilidi gerçek güvenliktir."
        [Required(ErrorMessage = "Onay metnini yazman gerekiyor.")]
        public string Dogrulama { get; set; } = string.Empty;

        // Düzenlemeye başlarken ekranda hangi sürüm vardı?
        //
        // ⚠️ İKİ SÜPERADMİN AYNI ANDA DÜZENLERSE İKİNCİSİ REDDEDİLİR.
        // Bu alan olmasaydı ikincinin kaydı, birincinin metnini hiç
        // görmeden üstüne yeni sürüm açardı — kimse bir şey kaybettiğini
        // fark etmezdi.
        public int BeklenenSurum { get; set; }
    }
}
