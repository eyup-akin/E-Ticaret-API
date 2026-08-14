namespace ETicaretAPI.Services
{
    // Email göndermenin SÖZLEŞMESİ (interface).
    // Controller sadece bunu tanır; arkada dev-konsol mu, Brevo mı, Resend mi
    // olduğunu BİLMEZ. Sağlayıcı değişince controller'a hiç dokunmayız.
    public interface IEmailGonderici
    {
        /// <summary>
        /// E-postayı gönderir; sağlayıcının mesaj kimliğini döndürür
        /// (yoksa null). Hata durumunda istisna fırlatır.
        /// </summary>
        /// <param name="olayAdi">
        /// Hangi bildirim (SiparisAlindi, SifreSifirlama…). Gönderim
        /// KARARINI etkilemez, yalnızca kayda yazılır.
        /// </param>
        //
        // ⭐ DEĞİŞTİ — iki ekleme, ikisi de EmailKaydi için:
        //
        //   • Dönüş tipi Task<string?> — Brevo'nun messageId'si destek
        //     talebinde tek dayanak; yutulursa "biz gönderdik" iddiasını
        //     doğrulayacak hiçbir şey kalmıyor.
        //   • olayAdi parametresi — kaydı yazan katman (KayitTutanEmailGonderici)
        //     bu arayüzün ARDINDA duruyor ve olayı başka türlü öğrenemezdi.
        //
        // ⚠️ Alternatif, kaydı GuvenliGonderAsync uzantısının içinde
        // yazmaktı; ama o STATİK bir metot, DbContext alamaz ve almasını
        // sağlamak 13 çağrı yerinin hepsini değiştirmek demekti.
        Task<string?> GonderAsync(
            string aliciEmail,
            string konu,
            string govdeHtml,
            string olayAdi);
    }
}
