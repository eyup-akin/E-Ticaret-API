namespace ETicaretAPI.Models
{
    public class Payment
    {
        public int Id { get; set; }

        public int OrderId { get; set; } // hangi siparişin ödemesi
        public int UserId { get; set; }  // ödeyen kullanıcı

        public decimal Amount { get; set; }              // ödenen tutar
        public string CardLast4 { get; set; } = string.Empty; // kullanılan kartın son 4 hanesi
        public string Status { get; set; } = "basarili";  // başarılı / başarısız

        public DateTime PaidAt { get; set; } = DateTime.UtcNow; // ödeme zamanı
    }
}