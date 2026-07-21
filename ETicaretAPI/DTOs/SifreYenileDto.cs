namespace ETicaretAPI.DTOs
{
    // "Yeni şifre belirle" — maildeki token + yeni şifre.
    public class SifreYenileDto
    {
        public string Token { get; set; } = string.Empty;
        public string YeniSifre { get; set; } = string.Empty;
    }
}