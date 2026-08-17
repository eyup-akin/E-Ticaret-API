namespace ETicaretAPI.Services
{
    // ⭐ YENİ — ödeme sağlayıcısı sözleşmesi.
    //
    // ⚠️ Iyzipay SDK'sının tipleri bu dosyanın dışına ÇIKMAZ. Çıksaydı
    // sağlayıcıyı değiştirmek imkânsızlaşır ve simülasyon yazılamazdı.
    // (IEmailGonderici'de alınan kararın aynısı.)

    public record OdemeAlicisi(
        string KullaniciId,
        string Ad,
        string Soyad,
        string Email,
        string Telefon,
        string KimlikNo,
        string Adres,
        string Sehir,
        string? Ip,
        DateTime KayitTarihi);

    public record OdemeAdresi(
        string AliciAdi,
        string Sehir,
        string Adres);

    public record OdemeBaslatIstegi(
        string ConversationId,
        int SiparisId,
        decimal Tutar,
        List<IyzicoSepetKalemi> Kalemler,
        List<int> Taksitler,
        string CallbackUrl,
        OdemeAlicisi Alici,
        OdemeAdresi TeslimatAdresi,
        // Kullanıcının iyzico kart anahtarı; null ise iyzico yeni üretir.
        string? CardUserKey);

    public record OdemeBaslatSonucu(
        bool Basarili,
        string? Token,
        DateTime? TokenGecerlilik,
        string? OdemeSayfasiUrl,
        string? HataKodu,
        string? HataMesaji,
        string? HamCevap);

    // Sorgu cevabındaki kalem kırılımı. PaymentTransactionId olmadan
    // kısmi iade yapılamıyor.
    public record OdemeSorguKalemi(
        string ItemId,
        string PaymentTransactionId,
        decimal Price,
        decimal PaidPrice);

    public record OdemeSorguSonucu(
        // Sorgu isteğinin kendisi başarılı mı (ağ/anahtar sorunu yok).
        bool CagriBasarili,
        // iyzico'nun ödeme durumu: SUCCESS / FAILURE / INIT_THREEDS ...
        string? OdemeDurumu,
        string? PaymentId,
        decimal? Price,
        decimal? PaidPrice,
        int Taksit,
        int? FraudDurumu,
        int? MdStatus,
        string? KartTipi,
        string? KartAilesi,
        string? BinNumarasi,
        string? Son4Hane,
        string? CardToken,
        string? CardUserKey,
        List<OdemeSorguKalemi> Kalemler,
        string? HataKodu,
        string? HataMesaji,
        string? HamCevap)
    {
        // iyzico başarılı bir ödemede "SUCCESS" döndürüyor.
        public bool OdemeBasarili =>
            CagriBasarili &&
            string.Equals(OdemeDurumu, "SUCCESS", StringComparison.OrdinalIgnoreCase);
    }

    public record OdemeIadeSonucu(
        bool Basarili,
        string? IyzicoIslemId,
        string? HataKodu,
        string? HataMesaji,
        string? HamCevap);


    public interface IOdemeSaglayici
    {
        Task<OdemeBaslatSonucu> BaslatAsync(OdemeBaslatIstegi istek);

        Task<OdemeSorguSonucu> SorgulaAsync(string token, string? conversationId = null);

        // Kısmi iade. ⚠️ paymentTransactionId KALEM bazlı, ödeme bazlı değil.
        Task<OdemeIadeSonucu> IadeEtAsync(
            string paymentTransactionId, decimal tutar, string? ip, string conversationId);

        // Tam iptal — yalnızca aynı gün ve tam tutar için geçerli.
        Task<OdemeIadeSonucu> IptalEtAsync(
            string paymentId, string? ip, string conversationId);

        // Saklı kartı sağlayıcıdan da siler. ⚠️ Yalnızca yerelden
        // silmek jetonu iyzico'da bırakır; müşteri "kartımı sildim"
        // dedikten sonra kart orada durmaya devam ederdi.
        Task<bool> KartSilAsync(string cardUserKey, string cardToken);
    }
}
