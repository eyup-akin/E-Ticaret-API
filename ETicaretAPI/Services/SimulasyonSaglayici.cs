using System.Collections.Concurrent;

namespace ETicaretAPI.Services
{
    // ⭐ YENİ — internetsiz/3DS'siz ödeme sağlayıcısı.
    //
    // ⚠️ NEDEN KORUNUYOR? Bu projede doğrulama "API'yi çalıştır, curl ile
    // senaryoyu geç" ile yapılıyor. Her siparişin gerçek 3DS istemesi bu
    // yöntemi öldürürdü. Email'deki "konsol" göndericisiyle aynı rol.
    //
    // ⚠️ Durum BELLEKTE. API yeniden başlarsa yarım kalan denemeler
    // kaybolur; süre aşımı işi onları zaten iptal ediyor. Kalıcı hale
    // getirmek, geliştirme aracına gerçek altyapı yazmak olurdu.
    public class SimulasyonSaglayici : IOdemeSaglayici
    {
        private record Deneme(
            string ConversationId,
            decimal Tutar,
            List<IyzicoSepetKalemi> Kalemler,
            string PaymentId)
        {
            public bool? Basarili { get; set; }
        }

        private readonly ConcurrentDictionary<string, Deneme> _denemeler = new();
        private readonly OdemeAyarlari _ayarlar;

        public SimulasyonSaglayici(OdemeAyarlari ayarlar)
        {
            _ayarlar = ayarlar;
        }

        public Task<OdemeBaslatSonucu> BaslatAsync(OdemeBaslatIstegi istek)
        {
            var token = "sim-" + Guid.NewGuid().ToString("N");

            _denemeler[token] = new Deneme(
                istek.ConversationId,
                istek.Tutar,
                istek.Kalemler,
                "sim-pay-" + Random.Shared.Next(100000, 999999));

            // Sahte ödeme sayfası. Tarayıcıda tıklanabilir, curl ile de
            // doğrudan sonuç ucuna POST atılabilir.
            var url = $"{_ayarlar.TabanAdres}/api/odeme/simulasyon?token={token}";

            return Task.FromResult(new OdemeBaslatSonucu(
                Basarili: true,
                Token: token,
                TokenGecerlilik: DateTime.UtcNow.AddMinutes(_ayarlar.BeklemeSuresiDk),
                OdemeSayfasiUrl: url,
                HataKodu: null,
                HataMesaji: null,
                HamCevap: "{\"saglayici\":\"simulasyon\"}"));
        }

        // Sahte ödeme sayfası sonucu buraya yazıyor; ardından gerçek
        // callback işleyicisi çalışıyor.
        public bool SonucBelirle(string token, bool basariliMi)
        {
            if (!_denemeler.TryGetValue(token, out var deneme))
            {
                return false;
            }

            deneme.Basarili = basariliMi;
            return true;
        }

        public bool TokenVar(string token) => _denemeler.ContainsKey(token);

        public Task<OdemeSorguSonucu> SorgulaAsync(string token, string? conversationId = null)
        {
            if (!_denemeler.TryGetValue(token, out var deneme))
            {
                return Task.FromResult(Bos("TOKEN_YOK", "Simülasyon token'ı bulunamadı."));
            }

            // Sonuç henüz seçilmediyse ödeme sürüyor demektir.
            if (deneme.Basarili == null)
            {
                return Task.FromResult(Bos(null, null) with { OdemeDurumu = "INIT_THREEDS" });
            }

            if (deneme.Basarili == false)
            {
                return Task.FromResult(Bos("SIM_RET", "Simülasyonda ödeme reddedildi.")
                    with { OdemeDurumu = "FAILURE", CagriBasarili = true });
            }

            var kalemler = deneme.Kalemler.Select(k => new OdemeSorguKalemi(
                ItemId: k.Id,
                PaymentTransactionId: "sim-tx-" + k.Id,
                Price: k.Tutar,
                PaidPrice: k.Tutar)).ToList();

            return Task.FromResult(new OdemeSorguSonucu(
                CagriBasarili: true,
                OdemeDurumu: "SUCCESS",
                PaymentId: deneme.PaymentId,
                Price: deneme.Tutar,
                PaidPrice: deneme.Tutar,
                Taksit: 1,
                FraudDurumu: 1,
                MdStatus: 1,
                KartTipi: "CREDIT_CARD",
                KartAilesi: "Simülasyon",
                BinNumarasi: "552879",
                Son4Hane: "0008",
                CardToken: null,
                CardUserKey: null,
                Kalemler: kalemler,
                HataKodu: null,
                HataMesaji: null,
                HamCevap: "{\"saglayici\":\"simulasyon\",\"status\":\"SUCCESS\"}"));
        }

        public Task<OdemeIadeSonucu> IadeEtAsync(
            string paymentTransactionId, decimal tutar, string? ip, string conversationId)
        {
            return Task.FromResult(new OdemeIadeSonucu(
                true, "sim-refund-" + Random.Shared.Next(100000, 999999),
                null, null, "{\"saglayici\":\"simulasyon\",\"islem\":\"iade\"}"));
        }

        public Task<OdemeIadeSonucu> IptalEtAsync(
            string paymentId, string? ip, string conversationId)
        {
            return Task.FromResult(new OdemeIadeSonucu(
                true, "sim-cancel-" + Random.Shared.Next(100000, 999999),
                null, null, "{\"saglayici\":\"simulasyon\",\"islem\":\"iptal\"}"));
        }

        public Task<bool> KartSilAsync(string cardUserKey, string cardToken) =>
            Task.FromResult(true);

        private static OdemeSorguSonucu Bos(string? hataKodu, string? hataMesaji) =>
            new(CagriBasarili: hataKodu == null, OdemeDurumu: null, PaymentId: null,
                Price: null, PaidPrice: null, Taksit: 1, FraudDurumu: null, MdStatus: null,
                KartTipi: null, KartAilesi: null, BinNumarasi: null, Son4Hane: null,
                CardToken: null, CardUserKey: null, Kalemler: new List<OdemeSorguKalemi>(),
                HataKodu: hataKodu, HataMesaji: hataMesaji, HamCevap: null);
    }
}
