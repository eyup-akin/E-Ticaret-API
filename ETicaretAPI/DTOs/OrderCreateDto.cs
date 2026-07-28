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
    }
}