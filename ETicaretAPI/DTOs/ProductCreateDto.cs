using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    public class ProductCreateDto
    {
        [Required(ErrorMessage = "Ürün adı boş olamaz!")]
        [StringLength(200, MinimumLength = 2, ErrorMessage = "Ürün adı 2-200 karakter olmalı!")]
        public string Name { get; set; } = string.Empty;

        [Range(0.01, 1000000, ErrorMessage = "Fiyat 0'dan büyük olmalı!")]
        public decimal Price { get; set; }

        [Range(0, 100000, ErrorMessage = "Stok negatif olamaz!")]
        public int Stock { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir kategori seçilmeli!")]
        public int CategoryId { get; set; }
    }
}