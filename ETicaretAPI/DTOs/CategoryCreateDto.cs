using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // Admin kategori eklerken/güncellerken (request)
    public class CategoryCreateDto
    {
        [Required(ErrorMessage = "Kategori adı boş olamaz!")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "Kategori adı 2-50 karakter olmalı!")]
        public string Name { get; set; } = string.Empty;
    }
}