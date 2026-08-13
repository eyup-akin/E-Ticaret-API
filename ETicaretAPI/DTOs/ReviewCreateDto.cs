using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // ⭐ DEĞİŞTİ — doğrulama controller'daki elle if'lerden buraya taşındı.
    //
    // ⚠️ NEDEN? Elle yazılan kontroller ModelState akışının DIŞINDA
    // kalıyordu: hata mesajı diğer tüm uçlardan farklı bir zarfla
    // dönüyordu. Öznitelik olunca [ApiController] onları otomatik
    // yakalıyor ve InvalidModelStateResponseFactory sayesinde
    // projenin standart { mesaj } biçimine giriyorlar.
    // (ProductCreateDto'daki KdvOraniGecerli ile aynı gerekçe.)
    public class ReviewCreateDto
    {
        [Range(1, 5, ErrorMessage = "Puan 1 ile 5 arasında olmalı!")]
        public int Rating { get; set; }

        // ⚠️ 1000, AppDbContext'teki HasMaxLength(1000) ile AYNI sayı.
        // Bu kolon daha önce nvarchar(max)'tı — projedeki tek sınırsız
        // metin alanıydı.
        //
        // ⚠️ MinimumLength 3: "a" ya da "." gibi tek karakterlik yorumlar
        // puanı taşıyor ama bilgi taşımıyor. Eski kod yalnızca boş olup
        // olmadığına bakıyordu.
        [Required(ErrorMessage = "Yorum boş olamaz!")]
        [StringLength(1000, MinimumLength = 3,
            ErrorMessage = "Yorum 3-1000 karakter olmalı!")]
        public string Comment { get; set; } = string.Empty;
    }
}
