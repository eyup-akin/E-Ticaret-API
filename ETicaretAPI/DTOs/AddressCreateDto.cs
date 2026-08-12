using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    public class AddressCreateDto
    {
        [Required(ErrorMessage = "Adres başlığı boş olamaz!")]
        [StringLength(50, ErrorMessage = "Başlık en fazla 50 karakter!")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Açık adres boş olamaz!")]
        public string FullAddress { get; set; } = string.Empty;

        [Required(ErrorMessage = "Şehir boş olamaz!")]
        public string City { get; set; } = string.Empty;

        // ⭐ DEĞİŞTİ (4.9) — artık numara METNİ değil, defterden SEÇİM.
        //
        // Eskiden burada serbest metin bir `Phone` alanı vardı ve her
        // adres kendi kopyasını taşıyordu. Artık müşteri "Numaralarım"
        // defterinden seçiyor; yeni bir numara girmek istiyorsa önce
        // POST /api/phones ile deftere ekliyor.
        //
        // ⚠️ Format doğrulaması BU DTO'DAN KALKTI — çünkü artık burada
        // bir format yok, bir id var. Numaranın geçerliliği tek yerde,
        // deftere girerken kontrol ediliyor. İki kapıda iki ayrı regex
        // tutmak, birini gevşetince diğerinin sessizce katı kalması
        // demekti.
        //
        // ⚠️ ZORUNLU (int, int? değil): kargo etiketi telefonsuz
        // basılamaz — kurye adresi bulamazsa arayacak. Modeldeki kolon
        // nullable ama o yalnızca "numara sonradan silindi" durumu
        // için; adres YARATIRKEN telefonsuzluk kabul edilmiyor.
        //
        // ⚠️ Sahiplik kontrolü burada YAPILAMAZ — DTO kimliği bilmez.
        // "Bu numara gerçekten bu kullanıcının mı?" sorusu
        // controller'da, sorgunun WHERE'ine girerek cevaplanıyor.
        // ⚠️ [Required] DEĞİL [Range] — bu bir tuzak. `int` bir değer
        // tipi; JSON'da hiç gönderilmezse alan 0 olarak doğar ve
        // [Required] "null değil" diye BAŞARILI sayar. Yani telefon
        // seçilmeden gelen istek doğrulamayı geçer, sonra veritabanında
        // olmayan 0 id'sini arardık. [Range(1, ...)] gerçekten kapatır.
        [Range(1, int.MaxValue, ErrorMessage = "Telefon numarası seçmelisin!")]
        public int PhoneId { get; set; }
    }
}