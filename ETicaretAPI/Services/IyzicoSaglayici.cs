using System.Globalization;
using System.Text.Json;
using Iyzipay;
using Iyzipay.Model;
using Iyzipay.Request;

namespace ETicaretAPI.Services
{
    // ⭐ YENİ — gerçek iyzico sağlayıcısı (Checkout Form).
    //
    // ⚠️ Kart numarası buraya da gelmiyor: kartı iyzico'nun kendi
    // sayfası topluyor, biz yalnızca sepeti ve alıcıyı gönderiyoruz.
    public class IyzicoSaglayici : IOdemeSaglayici
    {
        private readonly OdemeAyarlari _ayarlar;
        private readonly ILogger<IyzicoSaglayici> _log;

        public IyzicoSaglayici(OdemeAyarlari ayarlar, ILogger<IyzicoSaglayici> log)
        {
            _ayarlar = ayarlar;
            _log = log;
        }

        private Options Secenekler() => new()
        {
            ApiKey = _ayarlar.ApiAnahtari,
            SecretKey = _ayarlar.GizliAnahtar,
            BaseUrl = _ayarlar.TabanUrl
        };

        // ⚠️ InvariantCulture ŞART. Türkçe kültürde ondalık ayırıcı virgül
        // ve iyzico "349,90" gelen isteği reddediyor.
        private static string Para(decimal tutar) =>
            tutar.ToString("0.00", CultureInfo.InvariantCulture);

        private static decimal? ParaOku(string? metin) =>
            decimal.TryParse(metin, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                ? d : null;

        private static string Ham(object? nesne)
        {
            try
            {
                return JsonSerializer.Serialize(nesne);
            }
            catch
            {
                // Ham cevap yazılamadıysa akış durmasın; teşhis bilgisi
                // kaybolur ama ödeme kaybolmaz.
                return "{}";
            }
        }


        public async Task<OdemeBaslatSonucu> BaslatAsync(OdemeBaslatIstegi istek)
        {
            var talep = new CreateCheckoutFormInitializeRequest
            {
                Locale = Locale.TR.ToString(),
                ConversationId = istek.ConversationId,
                Price = Para(istek.Tutar),

                // ⚠️ PaidPrice = Price. Taksit komisyonunu iyzico kendi
                // ekliyor ve gerçek tutarı sorguda döndürüyor; burada
                // hesaplamaya çalışmak banka/kart ailesine göre değişen
                // bir oranı tahmin etmek olurdu.
                PaidPrice = Para(istek.Tutar),

                Currency = Currency.TRY.ToString(),
                BasketId = istek.SiparisId.ToString(),
                PaymentGroup = PaymentGroup.PRODUCT.ToString(),
                CallbackUrl = istek.CallbackUrl,
                EnabledInstallments = istek.Taksitler,

                // ⚠️ 3DS ZORUNLU. Kapalı bırakmak "gerçek gibi" hedefini
                // bozar ve sandbox'ta SMS adımı hiç görünmez.
                ForceThreeDS = 1,

                // null ise iyzico yeni anahtar üretip cevapta döndürüyor.
                CardUserKey = istek.CardUserKey,

                Buyer = new Buyer
                {
                    Id = istek.Alici.KullaniciId,
                    Name = istek.Alici.Ad,
                    Surname = istek.Alici.Soyad,
                    Email = istek.Alici.Email,
                    GsmNumber = istek.Alici.Telefon,

                    // ⚠️ TC kimlik numarası toplamıyoruz; sandbox'ta
                    // sabit değer kabul ediliyor. Canlıya çıkarken
                    // gerçekten toplanması gerekiyor (açık borç).
                    IdentityNumber = istek.Alici.KimlikNo,

                    RegistrationAddress = istek.Alici.Adres,
                    City = istek.Alici.Sehir,
                    Country = "Turkey",
                    Ip = istek.Alici.Ip ?? "0.0.0.0",
                    RegistrationDate = istek.Alici.KayitTarihi
                        .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                },

                ShippingAddress = Adres(istek.TeslimatAdresi),

                // Fatura adresi ayrı toplanmıyor; teslimat adresi
                // kullanılıyor. iyzico ikisini de zorunlu tutuyor.
                BillingAddress = Adres(istek.TeslimatAdresi),

                BasketItems = istek.Kalemler.Select(k => new BasketItem
                {
                    Id = k.Id,
                    Name = k.Ad,
                    Category1 = k.Kategori,
                    ItemType = (k.Fiziksel
                        ? BasketItemType.PHYSICAL
                        : BasketItemType.VIRTUAL).ToString(),
                    Price = Para(k.Tutar)
                }).ToList()
            };

            try
            {
                var cevap = await CheckoutFormInitialize.Create(talep, Secenekler());

                if (cevap == null || cevap.Status != "success")
                {
                    _log.LogWarning(
                        "iyzico CF initialize başarısız. kod: {Kod}, mesaj: {Mesaj}",
                        cevap?.ErrorCode, cevap?.ErrorMessage);

                    return new OdemeBaslatSonucu(false, null, null, null,
                        cevap?.ErrorCode, cevap?.ErrorMessage ?? "iyzico yanıt vermedi.",
                        Ham(cevap));
                }

                return new OdemeBaslatSonucu(
                    Basarili: true,
                    Token: cevap.Token,
                    TokenGecerlilik: cevap.TokenExpireTime.HasValue
                        ? DateTime.UtcNow.AddSeconds(cevap.TokenExpireTime.Value)
                        : DateTime.UtcNow.AddMinutes(_ayarlar.BeklemeSuresiDk),
                    OdemeSayfasiUrl: cevap.PaymentPageUrl,
                    HataKodu: null,
                    HataMesaji: null,
                    HamCevap: Ham(cevap));
            }
            catch (Exception ex)
            {
                // ⚠️ Yutulmuyor, sonuca çevriliyor: çağıran denemeyi
                // "basarisiz" yazıp müşteriye tekrar dene diyebilsin.
                _log.LogError(ex, "iyzico CF initialize istisnası.");
                return new OdemeBaslatSonucu(false, null, null, null,
                    "ISTISNA", "Ödeme başlatılamadı.", null);
            }
        }


        public async Task<OdemeSorguSonucu> SorgulaAsync(
            string token, string? conversationId = null)
        {
            var talep = new RetrieveCheckoutFormRequest
            {
                Locale = Locale.TR.ToString(),
                ConversationId = conversationId,
                Token = token
            };

            try
            {
                var cevap = await CheckoutForm.Retrieve(talep, Secenekler());

                if (cevap == null || cevap.Status != "success")
                {
                    return Basarisiz(cevap?.ErrorCode,
                        cevap?.ErrorMessage ?? "iyzico yanıt vermedi.", Ham(cevap),
                        cevap?.PaymentStatus);
                }

                var kalemler = (cevap.PaymentItems ?? new List<PaymentItem>())
                    .Where(k => !string.IsNullOrWhiteSpace(k.PaymentTransactionId))
                    .Select(k => new OdemeSorguKalemi(
                        ItemId: k.ItemId ?? "",
                        PaymentTransactionId: k.PaymentTransactionId!,
                        Price: ParaOku(k.Price) ?? 0m,
                        PaidPrice: ParaOku(k.PaidPrice) ?? 0m))
                    .ToList();

                return new OdemeSorguSonucu(
                    CagriBasarili: true,
                    OdemeDurumu: cevap.PaymentStatus,
                    PaymentId: cevap.PaymentId,
                    Price: ParaOku(cevap.Price),
                    PaidPrice: ParaOku(cevap.PaidPrice),
                    Taksit: cevap.Installment ?? 1,
                    FraudDurumu: cevap.FraudStatus,
                    MdStatus: cevap.MdStatus,
                    KartTipi: cevap.CardType,
                    KartAilesi: cevap.CardFamily,
                    BinNumarasi: cevap.BinNumber,
                    Son4Hane: cevap.LastFourDigits,
                    CardToken: cevap.CardToken,
                    CardUserKey: cevap.CardUserKey,
                    Kalemler: kalemler,
                    HataKodu: null,
                    HataMesaji: null,
                    HamCevap: Ham(cevap));
            }
            catch (Exception ex)
            {
                // ⚠️ Burada "başarısız ödeme" demiyoruz — SORGU başarısız.
                // İkisini karıştırmak, ödenmiş bir siparişi iptal etmeye
                // yol açardı. Webhook aynı sonucu tekrar getirecek.
                _log.LogError(ex, "iyzico CF retrieve istisnası. token: {Token}", token);
                return Basarisiz("ISTISNA", "Ödeme durumu sorgulanamadı.", null, null);
            }
        }


        public async Task<OdemeIadeSonucu> IadeEtAsync(
            string paymentTransactionId, decimal tutar, string? ip, string conversationId)
        {
            var talep = new CreateRefundRequest
            {
                Locale = Locale.TR.ToString(),
                ConversationId = conversationId,
                PaymentTransactionId = paymentTransactionId,
                Price = Para(tutar),
                Currency = Currency.TRY.ToString(),
                Ip = ip ?? "0.0.0.0"
            };

            try
            {
                var cevap = await Refund.Create(talep, Secenekler());

                return cevap != null && cevap.Status == "success"
                    ? new OdemeIadeSonucu(true, cevap.PaymentTransactionId, null, null, Ham(cevap))
                    : new OdemeIadeSonucu(false, null, cevap?.ErrorCode,
                        cevap?.ErrorMessage ?? "iyzico yanıt vermedi.", Ham(cevap));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "iyzico iade istisnası. tx: {Tx}", paymentTransactionId);
                return new OdemeIadeSonucu(false, null, "ISTISNA", "İade isteği gönderilemedi.", null);
            }
        }


        public async Task<OdemeIadeSonucu> IptalEtAsync(
            string paymentId, string? ip, string conversationId)
        {
            var talep = new CreateCancelRequest
            {
                Locale = Locale.TR.ToString(),
                ConversationId = conversationId,
                PaymentId = paymentId,
                Ip = ip ?? "0.0.0.0"
            };

            try
            {
                var cevap = await Cancel.Create(talep, Secenekler());

                return cevap != null && cevap.Status == "success"
                    ? new OdemeIadeSonucu(true, cevap.PaymentId, null, null, Ham(cevap))
                    : new OdemeIadeSonucu(false, null, cevap?.ErrorCode,
                        cevap?.ErrorMessage ?? "iyzico yanıt vermedi.", Ham(cevap));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "iyzico iptal istisnası. paymentId: {Id}", paymentId);
                return new OdemeIadeSonucu(false, null, "ISTISNA", "İptal isteği gönderilemedi.", null);
            }
        }


        public async Task<bool> KartSilAsync(string cardUserKey, string cardToken)
        {
            try
            {
                var cevap = await Iyzipay.Model.Card.Delete(new DeleteCardRequest
                {
                    Locale = Locale.TR.ToString(),
                    CardUserKey = cardUserKey,
                    CardToken = cardToken
                }, Secenekler());

                if (cevap?.Status != "success")
                {
                    _log.LogWarning("iyzico kart silme başarısız: {Mesaj}", cevap?.ErrorMessage);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "iyzico kart silme istisnası.");
                return false;
            }
        }


        private static Iyzipay.Model.Address Adres(OdemeAdresi adres) => new()
        {
            ContactName = adres.AliciAdi,
            City = adres.Sehir,
            Country = "Turkey",
            Description = adres.Adres
        };

        private static OdemeSorguSonucu Basarisiz(
            string? kod, string? mesaj, string? ham, string? odemeDurumu) =>
            new(CagriBasarili: false, OdemeDurumu: odemeDurumu, PaymentId: null,
                Price: null, PaidPrice: null, Taksit: 1, FraudDurumu: null, MdStatus: null,
                KartTipi: null, KartAilesi: null, BinNumarasi: null, Son4Hane: null,
                CardToken: null, CardUserKey: null, Kalemler: new List<OdemeSorguKalemi>(),
                HataKodu: kod, HataMesaji: mesaj, HamCevap: ham);
    }
}
