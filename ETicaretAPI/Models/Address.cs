namespace ETicaretAPI.Models
{
    public class Address
    {
        public int Id { get; set; }

        // Adresin sahibi — User tablosuna bağlanır
        public int UserId { get; set; }

        public string Title { get; set; } = string.Empty;       // Ev, İş vb.
        public string FullAddress { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
    }
}