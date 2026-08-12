using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // ⭐ YENİ (4.9) — okuma tarafı
    public class PhoneDto
    {
        public int Id { get; set; }

        // Kanonik hali: "5528083129". İstemci bunu bir daha
        // biçimlendirmek zorunda kalmasın diye Gorunum ile birlikte
        // gidiyor — ama ikisi de AYNI kaynaktan türüyor, istemcide
        // ikinci bir formatlayıcı yok.
        public string Numara { get; set; } = string.Empty;

        // "0552 808 31 29" — sunucuda türetiliyor, saklanmıyor.
        //
        // ⚠️ Neden istemcide biçimlendirilmiyor? İki istemci var
        // (mobil + admin) ve her birine ayrı bir formatlayıcı
        // yazmak, aynı kuralın iki kopyası demekti. Kural
        // değişirse (örn. ülke kodu gösterelim) biri unutulurdu.
        public string Gorunum { get; set; } = string.Empty;

        public string Etiket { get; set; } = string.Empty;
        public bool DogrulandiMi { get; set; }
        public bool VarsayilanMi { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // ⭐ YENİ (4.9) — yazma tarafı
    public class PhoneCreateDto
    {
        // ⚠️ Format DOĞRULAMASI burada gevşek, asıl kontrol
        // TelefonBicimi.Normalize'da. Buradaki regex sadece
        // "bu alan telefon değil" durumunu (harf, boş, tek hane)
        // ucuza eler; "10 haneye iniyor mu?" sorusunun cevabı
        // normalizasyondan sonra belli olur ve controller onu
        // ayrıca kontrol eder.
        [Required(ErrorMessage = "Telefon numarası boş olamaz!")]
        [RegularExpression(@"^\+?[0-9\s\-\(\)\.]{10,20}$",
            ErrorMessage = "Geçerli bir telefon numarası gir (örn: 0532 123 45 67)")]
        public string Numara { get; set; } = string.Empty;

        // ⚠️ Zorunlu — ve bu bir külfet değil: mobil form hazır
        // etiketler (Cep / İş / Ev) sunuyor, müşteri tek dokunuşla
        // seçiyor. Boş bırakılabilseydi liste "0552... / 0533..."
        // diye iki numaradan hangisinin ne olduğu anlaşılmayan bir
        // yığına dönerdi — adres başlığındaki dersin aynısı.
        [Required(ErrorMessage = "Etiket boş olamaz! (Cep, İş, Ev...)")]
        [StringLength(30, ErrorMessage = "Etiket en fazla 30 karakter!")]
        public string Etiket { get; set; } = string.Empty;
    }
}
