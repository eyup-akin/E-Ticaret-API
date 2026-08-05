using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // Sipariş oluştururken kullanıcı bunları gönderir
    public class OrderCreateDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir adres seçilmeli!")]
        public int AddressId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir kart seçilmeli!")]
        public int CardId { get; set; }

        // Kupon kodu — isteğe bağlı. Boş/null = kupon kullanılmıyor.
        //
        // ⚠️ Sadece KOD alıyoruz. İndirim tutarını ön yüzden ALMIYORUZ,
        // sunucu yeniden hesaplıyor.
        public string? CouponCode { get; set; }

        // ⭐ YENİ — sipariş notu, isteğe bağlı.
        //
        // Neden [Required] yok: notsuz sipariş tamamen normal.
        //
        // Neden [MaxLength] VAR: sınırsız metin kabul etmek üç riski
        // birden taşır — veritabanını şişirmek, kargo etiketini bozmak
        // ve admin ekranını okunmaz hale getirmek. 500 karakter
        // "kapıya bırakın, zili çalmayın" için fazlasıyla yeterli.
        //
        // Bu sınır AppDbContext'teki HasMaxLength(500) ile aynı sayı
        // olmalı. Farklı olsalardı ya DTO gereksiz yere reddederdi ya
        // da veritabanı istisna fırlatırdı — ikisi de kötü.
        [MaxLength(500, ErrorMessage = "Sipariş notu en fazla 500 karakter olabilir!")]
        public string? CustomerNote { get; set; }

        // ⭐ YENİ — çift sipariş koruması anahtarı.
        //
        // Neden [Required] yok: anahtarsız istek geçerlidir, sadece
        // korumasızdır. Zorunlu yapsaydık Postman'den test etmek ve
        // ileride başka istemciler eklemek zorlaşırdı.
        //
        // Neden [MaxLength(64)]: AppDbContext'teki HasMaxLength(64)
        // ile AYNI sayı olmalı. Farklı olsalardı ya DTO gereksiz yere
        // reddederdi ya da veritabanı istisna fırlatırdı.
        //
        // ⚠️ Sınır aynı zamanda bir savunma: sınırsız metin kabul
        // eden bir kolona index kurulu olduğu için, uzun değerler
        // index'i şişirirdi.
        [MaxLength(64, ErrorMessage = "Geçersiz istek anahtarı!")]
        public string? IdempotencyKey { get; set; }


    }
}