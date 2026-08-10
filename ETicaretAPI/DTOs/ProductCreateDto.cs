using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    public class ProductCreateDto
    {
        [Required(ErrorMessage = "Ürün adı boş olamaz!")]
        [StringLength(200, MinimumLength = 2, ErrorMessage = "Ürün adı 2-200 karakter olmalı!")]
        public string Name { get; set; } = string.Empty;

        // ⭐ YENİ — barkod artık ZORUNLU. Yeni üründe boş bırakılamaz.
        [Required(ErrorMessage = "Barkod zorunlu!")]
        [StringLength(64, MinimumLength = 1, ErrorMessage = "Barkod 1-64 karakter olmalı!")]
        public string Barcode { get; set; } = string.Empty;

        [Range(0.01, 1000000, ErrorMessage = "Fiyat 0'dan büyük olmalı!")]
        public decimal Price { get; set; }

        // ⭐ YENİ — maliyet. 0 olabilir (promosyon ürünü) ama negatif olamaz.
        [Range(0, 1000000, ErrorMessage = "Maliyet negatif olamaz!")]
        public decimal Cost { get; set; }

        [Range(0, 100000, ErrorMessage = "Stok negatif olamaz!")]
        public int Stock { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir kategori seçilmeli!")]
        public int CategoryId { get; set; }

        // ⭐ YENİ — ürün satışa açık mı?
        //
        // Neden [Range] veya [Required] yok:
        // bool'un iki değeri de geçerli. Doğrulanacak bir şey yok.
        //
        // Neden = true varsayılanı KRİTİK:
        // Bu bir "girdi" DTO'su. Admin paneli henüz güncellenmediği için
        // gönderdiği JSON'da isActive alanı YOK. JSON'da olmayan alan
        // deserialize edilirken C# başlangıç değerinde kalır. Varsayılanı
        // false yapsaydık, panelden kaydedilen HER ürün sessizce pasife
        // düşerdi. true yazarak "bilgi gelmediyse ürün satıştadır" demiş
        // oluyoruz — geriye dönük uyumluluk.
        public bool IsActive { get; set; } = true;

        // ⭐ YENİ — ürün açıklaması (isteğe bağlı).
        //
        // [Required] yok: açıklamasız ürün geçerlidir.
        //
        // [MaxLength(2000)] AppDbContext'teki HasMaxLength(2000) ile
        // aynı sayı. Doğrulama burada yapılınca kullanıcı anlaşılır
        // bir hata mesajı alır; sadece veritabanına bıraksaydık
        // ham bir SQL istisnası patlardı.
        [MaxLength(2000, ErrorMessage = "Açıklama en fazla 2000 karakter olabilir!")]
        public string? Description { get; set; }

        // ⭐ YENİ — KDV ORANI (%)
        //
        // ⚠️ NEDEN [Range(0, 100)] DEĞİL, BEYAZ LİSTE?
        //
        // Range her ara değeri kabul ederdi: %7, %13, %99... Bunlar
        // Türkiye'de geçerli oranlar DEĞİL. Yanlış oranla kesilmiş
        // fatura, hesap hatası değil VERGİ HATASIDIR.
        //
        // Doğrulama SUNUCUDA yapılıyor. Panelin açılır menü göstermesi
        // yetmez — istek Postman'den, mobilden, betikten de gelebilir.
        // "Beyaz liste doğrulaması sunucuda yapılır."
        //
        // ⚠️ NEDEN = 20 VARSAYILANI KRİTİK?
        //
        // IsActive'deki gerekçenin aynısı: bu bir girdi DTO'su ve admin
        // paneli henüz güncellenmediği için gönderdiği JSON'da vatRate
        // alanı YOK. JSON'da olmayan alan C# başlangıç değerinde kalır.
        // Varsayılanı 0 bıraksaydık panelden kaydedilen HER ürün
        // sessizce %0 KDV'ye düşerdi — ve doğrulama da onu reddederdi,
        // yani panel tamamen çalışmaz hale gelirdi.
        [KdvOraniGecerli]
        public int VatRate { get; set; } = 20;

        // ⭐ YENİ (B1) — indirim öncesi fiyat (isteğe bağlı).
        //
        // ⚠️ DOĞRULAMA BURADA DEĞİL, CONTROLLER'DA.
        // Kural iki alan ARASINDA bir ilişki (EskiFiyat > Price) ve
        // veri anotasyonları tek alana bakar. Attribute yazmaya
        // çalışmak kuralı yarım uygulardı.
        public decimal? EskiFiyat { get; set; }
    }


    // ⭐ YENİ — KDV oranı beyaz liste doğrulayıcısı.
    //
    // ⚠️ NEDEN ÖZEL BİR ÖZNİTELİK, NEDEN CONTROLLER'DA if DEĞİL?
    //
    // Bu kural İKİ yerde gerekiyor: ürün ekleme ve ürün güncelleme
    // (ikisi de ProductCreateDto kullanıyor). Ayrıca ileride Excel
    // içe aktarma da aynı kontrolü yapacak.
    //
    // Controller'da if olarak yazsaydık, ModelState akışının dışında
    // kalırdı: hata mesajı diğer doğrulama hatalarından farklı bir
    // biçimde dönerdi ve ön yüzün iki ayrı hata formatı işlemesi
    // gerekirdi.
    //
    // Öznitelik olarak yazınca [ApiController] bunu otomatik yakalıyor
    // ve diğer tüm doğrulama hatalarıyla aynı zarfta döndürüyor.
    public class KdvOraniGecerliAttribute : ValidationAttribute
    {
        // Türkiye'de yürürlükteki KDV oranları.
        //
        // ⚠️ Oranlar yasayla değişir. Değiştiği gün burası güncellenir
        // ve ESKİ SİPARİŞLER ETKİLENMEZ — çünkü oran OrderItem'a
        // dondurulmuş durumda. Dondurmasaydık, bu diziden bir oranı
        // çıkardığımız an geçmiş faturalar geçersiz hale gelirdi.
        private static readonly int[] GecerliOranlar = { 1, 10, 20 };

        public override bool IsValid(object? value)
        {
            if (value is not int oran)
            {
                return false;
            }

            return GecerliOranlar.Contains(oran);
        }

        public override string FormatErrorMessage(string name)
        {
            return "KDV oranı yalnızca %1, %10 veya %20 olabilir!";
        }
    }
}