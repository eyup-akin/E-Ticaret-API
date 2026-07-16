namespace ETicaretAPI.DTOs
{
    public class ReviewCreateDto
    {
        public int Rating { get; set; }         // 1-5
        public string Comment { get; set; } = string.Empty;
    }
}