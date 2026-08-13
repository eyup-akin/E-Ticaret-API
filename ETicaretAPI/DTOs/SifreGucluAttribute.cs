using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // ⭐ YENİ — ŞİFRE KURALI, TEK YERDE
    //
    // ⚠️ NEDEN ÖZEL BİR ÖZNİTELİK, NEDEN HER DTO'YA [MinLength(6)]?
    //
    // Kural ÜÇ yerde geçiyor: kayıt, şifre sıfırlama, şifre değiştirme.
    // Üçüne ayrı ayrı yazsaydık — ki zaten öyleydi — biri güncellenip
    // diğeri unutulurdu. Nitekim tam olarak bu oldu:
    //
    //   reset-password   → 6 karakter şartı VAR (elle if ile)
    //   change-password  → 6 karakter şartı VAR ([MinLength])
    //   register         → HİÇBİR ŞART YOK
    //
    // Yani şifrenin İLK KEZ belirlendiği yer, tek korumasız yerdi:
    // tek karakterli şifreyle hesap açılabiliyordu. Kural buraya
    // toplanınca yeni bir şifre alanı eklemek de bu özniteliği
    // yazmaktan ibaret hale geliyor.
    //
    // ⚠️ NEDEN CONTROLLER'DA if DEĞİL?
    // KdvOraniGecerliAttribute ile aynı gerekçe: controller'daki elle
    // kontrol ModelState akışının dışında kalır ve hata mesajı diğer
    // doğrulama hatalarından FARKLI bir zarfla döner. Öznitelik olunca
    // [ApiController] onu otomatik yakalıyor ve
    // InvalidModelStateResponseFactory sayesinde diğerleriyle aynı
    // { mesaj } biçiminde çıkıyor.
    public class SifreGucluAttribute : ValidationAttribute
    {
        // ⚠️ Bu sayıyı büyütmek TEK satırlık bir iş — kuralın tek yerde
        // yaşamasının bütün amacı bu.
        //
        // 6 seçildi çünkü mevcut iki kural zaten 6 diyor. 8'e çıkarmak
        // güvenlik açısından daha iyi olurdu ama mevcut kullanıcıların
        // şifre DEĞİŞTİREMEZ hale gelmesi anlamına gelirdi (eski
        // şifreleri kuralı sağlamıyor). Sıkılaştırma yapılacaksa
        // kullanıcılara önceden haber verilmeli.
        public const int EnAzUzunluk = 6;

        public override bool IsValid(object? value)
        {
            // ⚠️ null'ı GEÇERLİ sayıyoruz — bilerek.
            //
            // "Alan var mı?" sorusu [Required]'ın işi. Burada da
            // reddetseydik null bir şifre İKİ hata mesajı üretirdi
            // ("Şifre gerekli. Şifre en az 6 karakter olmalı.").
            // Her öznitelik tek bir soruya cevap verir.
            if (value is null)
            {
                return true;
            }

            if (value is not string sifre)
            {
                return false;
            }

            // ⚠️ Trim EDİLMİYOR: boşluk şifrenin meşru bir parçası
            // olabilir ve kırpmak kullanıcının belirlediği şifreyi
            // sessizce değiştirmek olurdu. Ama TAMAMEN boşluktan
            // oluşan bir şifre kazadır, onu eliyoruz.
            return !string.IsNullOrWhiteSpace(sifre)
                && sifre.Length >= EnAzUzunluk;
        }

        public override string FormatErrorMessage(string name)
        {
            return $"Şifre en az {EnAzUzunluk} karakter olmalı biladerim!";
        }
    }
}
