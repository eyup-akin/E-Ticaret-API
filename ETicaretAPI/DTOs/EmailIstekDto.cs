namespace ETicaretAPI.DTOs
{
    // Sadece email alan istekler için (doğrulama linkini yeniden gönder).
    //
    // Not: SifreSifirlamaIstekDto ile alan olarak aynı ama ismi ona özel.
    // İki farklı iş için tek DTO paylaşmak, ileride biri değişince (örn.
    // birine "captcha" alanı eklenince) diğerini de etkiler. Ayrı tutuyoruz.
    public class EmailIstekDto
    {
        public string Email { get; set; } = string.Empty;
    }
}