using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // Müşteri sepette kupon denerken gönderdiği şey.
    //
    // ⚠️ SADECE KOD var. İndirim tutarı, sepet toplamı gibi bilgiler
    // İSTENMİYOR — hepsini sunucu kendisi hesaplar. Ön yüzden gelen
    // para bilgisine güvenmiyoruz.
    public class CouponValidateDto
    {
        [Required(ErrorMessage = "Kupon kodu gerekli.")]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;
    }
}