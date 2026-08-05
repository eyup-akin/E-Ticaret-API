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
    }
}