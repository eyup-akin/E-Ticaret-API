namespace ETicaretAPI.Services
{
    // ⭐ YENİ — appsettings "Odeme" bölümünü tipli okur.
    // MagazaAyarlari ile aynı desen ve aynı gerekçe: anahtar adı tek
    // yerde yazılı olsun, yazım hatası sessizce null dönmesin.
    public class OdemeAyarlari
    {
        private readonly IConfiguration _config;
        private readonly MagazaAyarlari _magaza;

        public OdemeAyarlari(IConfiguration config, MagazaAyarlari magaza)
        {
            _config = config;
            _magaza = magaza;
        }

        // "simulasyon" | "iyzico"
        public string Saglayici =>
            (_config["Odeme:Saglayici"] ?? "simulasyon").Trim().ToLowerInvariant();

        public bool IyzicoMu => Saglayici == "iyzico";

        public string ApiAnahtari => _config["Odeme:Iyzico:ApiAnahtari"] ?? "";
        public string GizliAnahtar => _config["Odeme:Iyzico:GizliAnahtar"] ?? "";

        public string TabanUrl =>
            _config["Odeme:Iyzico:TabanUrl"] ?? "https://sandbox-api.iyzipay.com";

        // Ödenmemiş sipariş kaç dakika sonra iptal edilir.
        // ⚠️ En az 5: daha kısası müşteri 3DS'i bitirmeden siparişi iptal eder.
        public int BeklemeSuresiDk
        {
            get
            {
                var deger = _config.GetValue<int?>("Odeme:BeklemeSuresiDk") ?? 30;
                return Math.Clamp(deger, 5, 240);
            }
        }

        // Callback ve dönüş adresleri buradan türetiliyor.
        // ⚠️ Bu adres dışarıdan erişilebilir HTTPS olmalı, yoksa iyzico
        // callback'i hiç ulaşmaz ve sipariş odeme_bekliyor kalır.
        public string TabanAdres =>
            (_config["Uygulama:TabanUrl"] ?? "http://localhost:5289").TrimEnd('/');

        // ⚠️ Taksit listesi sipariş tutarından TÜRETİLİYOR, istemciden
        // gelmiyor. "Ön yüze güvenme" kuralının ödeme tarafı.
        public List<int> TaksitSecenekleri(decimal tutar)
        {
            return tutar >= _magaza.TaksitAltSiniri
                ? new List<int> { 1, 2, 3, 6, 9 }
                : new List<int> { 1 };
        }
    }
}
