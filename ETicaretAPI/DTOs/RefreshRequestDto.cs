namespace ETicaretAPI.DTOs
{
    // Refresh ve logout istekleri için — istemci HAM refresh token'ı gönderir.
    public class RefreshRequestDto
    {
        public string RefreshToken { get; set; } = string.Empty;
    }
}