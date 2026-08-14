using System.Text;
using System.Text.Json;

namespace ETicaretAPI.Services
{
    // ⭐ YENİ — GERÇEK E-POSTA GÖNDERİCİSİ (Brevo)
    //
    // ⚠️ NEDEN SMTP DEĞİL, HTTP API?
    //
    // Brevo ikisini de sunuyor. HTTP API seçildi çünkü:
    //   • Ev/ofis internetlerinde 25 ve sık sık 587 portu dışarı
    //     KAPALI. SMTP bu ortamda sessizce ölür ve sebebi "mail
    //     gitmiyor" diye günlerce aranır. 443 hiçbir yerde kapalı değil.
    //   • SMTP bir NuGet paketi (MailKit) gerektirirdi; HTTP için
    //     IHttpClientFactory zaten kayıtlı.
    //   • Docker/Caddy/Tailscale kurulumunda hiçbir port açılmıyor:
    //     bu giden bir istek, gelen trafikle ilgisi yok.
    //
    // ⚠️ NEDEN BREVO, NEDEN RESEND DEĞİL?
    //
    // Resend gönderen kimliğini ALAN ADI üzerinden doğruluyor ve
    // henüz satın alınmış bir alan adımız yok — `.ts.net` bize ait
    // olmadığı için DNS kaydı ekleyemiyoruz. Brevo tek bir gönderen
    // ADRESİNİ doğrulatıyor (adrese onay maili gönderip tıklatarak),
    // alan adı istemiyor. Alan adı alınınca yalnızca yapılandırmadaki
    // GonderenAdres değişecek, bu dosya değişmeyecek.
    public class BrevoEmailGonderici : IEmailGonderici
    {
        // Adlandırılmış HttpClient'ın adı. Program.cs'teki kayıtla
        // birebir aynı olmak zorunda; iki yerde geçtiği için sabit.
        // (Yazım hatası yapılırsa yapılandırılmamış bir client gelir
        //  ve timeout 100 sn'ye döner — patlamaz, sadece yanlış çalışır.)
        public const string IstemciAdi = "brevo";

        private const string Adres = "https://api.brevo.com/v3/smtp/email";

        private readonly IHttpClientFactory _fabrika;
        private readonly IConfiguration _config;
        private readonly ILogger<BrevoEmailGonderici> _log;

        public BrevoEmailGonderici(
            IHttpClientFactory fabrika,
            IConfiguration config,
            ILogger<BrevoEmailGonderici> log)
        {
            _fabrika = fabrika;
            _config = config;
            _log = log;
        }

        // ⭐ DEĞİŞTİ — messageId döndürüyor ve olayAdi alıyor (EmailKaydi için).
        // ⚠️ olayAdi burada KULLANILMIYOR: gönderim kararını etkilemiyor,
        // yalnızca arayüz sözleşmesinin parçası. Kaydı yazan katman
        // KayitTutanEmailGonderici.
        public async Task<string?> GonderAsync(
            string aliciEmail, string konu, string govdeHtml, string olayAdi)
        {
            var apiAnahtari = _config["Email:ApiAnahtari"] ?? string.Empty;
            var gonderenAdres = _config["Email:GonderenAdres"] ?? string.Empty;

            // ⚠️ Gönderen adı boşsa mağaza adına düşüyor: "Satık" bilgisi
            // appsettings'te TEK yerde (Magaza:Ad) dursun. İki yere
            // yazsaydık biri değişip diğeri kalırdı ve müşteri iki
            // farklı isimden mail alırdı.
            var gonderenAd = _config["Email:GonderenAd"];

            if (string.IsNullOrWhiteSpace(gonderenAd))
            {
                gonderenAd = _config["Magaza:Ad"] ?? "Mağaza";
            }

            // ⚠️⚠️ GÜVENLİK VALFİ — GELİŞTİRMEDE HAYAT KURTARIR.
            //
            // Veritabanında gerçek olabilecek adreslerle test
            // kullanıcıları var (deneme@gmail.com gibi). Eski bir test
            // siparişinin durumunu değiştirmek, tanımadığımız birine
            // "siparişiniz kargoya verildi" maili yollardı. Bu ayar
            // doluyken TÜM mailler tek bir adrese gider ve gerçek alıcı
            // konunun başına yazılır — kime gideceğini görürsün ama
            // kimse rahatsız olmaz.
            //
            // ⚠️ Alıcıyı değiştirmek yerine göndermeyi tamamen kapatmak
            // da mümkündü ama o zaman HTML'in gerçek bir posta
            // kutusunda nasıl göründüğü hiç test edilemezdi — bu
            // entegrasyonun asıl doğrulaması tam olarak o.
            var yonlendirme = _config["Email:TumunuSuAdreseYonlendir"];

            if (!string.IsNullOrWhiteSpace(yonlendirme))
            {
                konu = "[→ " + aliciEmail + "] " + konu;
                aliciEmail = yonlendirme.Trim();
            }

            var istekGovdesi = new
            {
                sender = new { name = gonderenAd, email = gonderenAdres },
                to = new[] { new { email = aliciEmail } },
                subject = konu,
                htmlContent = govdeHtml,
            };

            var istemci = _fabrika.CreateClient(IstemciAdi);

            using var istek = new HttpRequestMessage(HttpMethod.Post, Adres)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(istekGovdesi),
                    Encoding.UTF8,
                    "application/json"),
            };

            // ⚠️ Anahtar "Authorization" başlığında DEĞİL, Brevo'ya özel
            // "api-key" başlığında. Bearer şemasıyla gönderilirse 401
            // döner ve hata mesajı bunu söylemez.
            istek.Headers.Add("api-key", apiAnahtari);
            istek.Headers.Add("accept", "application/json");

            var cevap = await istemci.SendAsync(istek);

            if (!cevap.IsSuccessStatusCode)
            {
                // ⚠️ HATA YUTULMUYOR, FIRLATILIYOR — bilinçli.
                //
                // Çağıran taraf zaten GuvenliGonderAsync ile korunuyor
                // ve orada yutmanın gerekçesi uzun uzun yazılı. Burada
                // ikinci kez yutsaydık hata İKİ katmanda birden
                // kaybolur, günlükte hiçbir iz kalmazdı.
                var detay = await cevap.Content.ReadAsStringAsync();

                // ⚠️ API anahtarı loglanmıyor, cevap gövdesi loglanıyor.
                // Brevo hatanın sebebini burada açıkça yazıyor
                // (doğrulanmamış gönderen, kota, geçersiz anahtar) ve
                // bu bilgi olmadan sorun kör aranır.
                _log.LogError(
                    "Brevo reddetti. Durum: {Durum}, Cevap: {Cevap}",
                    (int)cevap.StatusCode, detay);

                throw new HttpRequestException(
                    "Brevo e-postayı kabul etmedi (HTTP " + (int)cevap.StatusCode + ").");
            }

            // ⭐ YENİ — SAĞLAYICI MESAJ KİMLİĞİ.
            //
            // Brevo başarılı cevapta {"messageId":"<...@smtp-relay...>"}
            // döndürüyor. Bu kimlik, "biz gönderdik" iddiasının tek
            // dayanağı: Brevo panelindeki teslimat kaydıyla eşleştirmek
            // ancak onunla mümkün.
            //
            // ⚠️ Ayrıştırma HATASI GÖNDERİMİ BOZMAMALI — mail çoktan
            // gitti. Kimliği okuyamamak bir kayıt eksikliği, gönderim
            // hatası değil; istisna fırlatsaydık çağıran taraf gitmiş bir
            // maili "gitmedi" diye kaydederdi.
            try
            {
                using var belge = JsonDocument.Parse(
                    await cevap.Content.ReadAsStringAsync());

                if (belge.RootElement.TryGetProperty("messageId", out var kimlik))
                {
                    return kimlik.GetString();
                }
            }
            catch (JsonException)
            {
                _log.LogWarning("Brevo cevabından messageId okunamadı.");
            }

            return null;
        }
    }
}
