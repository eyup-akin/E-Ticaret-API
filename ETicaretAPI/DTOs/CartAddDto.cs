using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    public class CartAddDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir ürün seçilmeli!")]
        public int ProductId { get; set; }

        [Range(1, 1000, ErrorMessage = "Adet en az 1 olmalı!")]
        public int Quantity { get; set; }
    }
}