namespace ETicaretAPI.Services
{
    // Email göndermenin SÖZLEŞMESİ (interface).
    // Controller sadece bunu tanır; arkada dev-konsol mu, Brevo mı, Resend mi
    // olduğunu BİLMEZ. Sağlayıcı değişince controller'a hiç dokunmayız.
    public interface IEmailGonderici
    {
        Task GonderAsync(string aliciEmail, string konu, string govdeHtml);
    }
}