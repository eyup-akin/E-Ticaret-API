namespace ETicaretAPI.DTOs
{
    // "Şifremi unuttum" — sadece email lazım.
    public class SifreSifirlamaIstekDto
    {
        public string Email { get; set; } = string.Empty;
    }
}