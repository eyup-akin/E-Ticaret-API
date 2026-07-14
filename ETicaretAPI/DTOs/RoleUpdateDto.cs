using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    public class RoleUpdateDto
    {
        [Required(ErrorMessage = "Rol boş olamaz!")]
        public string Role { get; set; } = string.Empty;
    }
}