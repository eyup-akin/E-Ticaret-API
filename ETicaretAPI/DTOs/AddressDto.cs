namespace ETicaretAPI.DTOs
{
    public class AddressDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string FullAddress { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
    }
}