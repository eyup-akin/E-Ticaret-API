using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    public class CartAddDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir ürün seçilmeli!")]
        public int ProductId { get; set; }

        // Üst sınır 99: mobil arayüzdeki adet seçici de 99'da duruyor.
        // Üç katman (arayüz, DTO, veritabanı kırpması) aynı sayıyı bilmeli;
        // farklı olsalar arayüzde kabul edilen bir değer sunucuda reddedilirdi.
        [Range(1, 99, ErrorMessage = "Adet 1 ile 99 arasında olmalı!")]
        public int Quantity { get; set; }
    }
}