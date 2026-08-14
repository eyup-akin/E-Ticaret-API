using ETicaretAPI.Services;
using ETicaretAPI.Support;

namespace ETicaretAPI.Middleware
{
    // GLOBAL HATA YAKALAYICI
    // Her istek buradan geçer. Alt katmanlarda (controller, servis, EF Core)
    // yakalanmamış bir hata patlarsa burada tutulur ve düzgün JSON'a çevrilir.
    // Böylece istemciye ASLA stack trace / HTML gitmez.
    public class HataYakalamaMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<HataYakalamaMiddleware> _logger;
        private readonly IHostEnvironment _env;

        // ⭐ YENİ — hatayı tabloya da yazan servis.
        //
        // ⚠️ Singleton alınabiliyor çünkü SistemGunlugu kendi kapsamını
        // açıyor. Doğrudan AppDbContext alsaydık patlardı: middleware
        // singleton, DbContext scoped.
        private readonly SistemGunlugu _gunluk;

        public HataYakalamaMiddleware(
            RequestDelegate next,
            ILogger<HataYakalamaMiddleware> logger,
            IHostEnvironment env,
            SistemGunlugu gunluk)
        {
            _next = next;
            _logger = logger;
            _env = env;
            _gunluk = gunluk;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // İsteği bir sonraki katmana devret (normal akış)
                await _next(context);
            }
            catch (Exception ex)
            {
                // Buraya düştüysek bir yerde bir şey patladı
                _logger.LogError(ex, "Yakalanmamış hata oluştu");

                // ⭐ YENİ — HATAYI TABLOYA DA YAZ.
                //
                // ⚠️ Konteyner yeniden başlayınca ILogger çıktısı
                // kayboluyordu; panelden bakılabilen kalıcı bir iz yoktu.
                //
                // ⚠️ Yazma KENDİ KAPSAMINDA ve hata YUTULUYOR
                // (SistemGunlugu). İki sebep: (1) tetikleyen transaction
                // geri alınıyor, kaydı ona bağlamak kaydı da silerdi;
                // (2) log yazarken çıkan hata tekrar log yazmaya
                // çalışsaydı sonsuz döngü olurdu.
                await _gunluk.HataYazAsync(
                    yol: context.Request.Path.Value ?? "",
                    yontem: context.Request.Method,
                    mesaj: ex.Message,

                    // ⚠️ Yığın izi tabloya giriyor — bilinçli karar.
                    // Ekran yalnızca süperadmine açık; izsiz bir kayıt
                    // "bir şey patladı" demekten öteye gitmez ve
                    // konteyner günlüğüne bakma zorunluluğunu kaldırmaz,
                    // ki bu tabloyu açmanın sebebi tam olarak buydu.
                    yiginIzi: ex.ToString(),

                    kullaniciId: KullaniciId(context),
                    ip: IstemciAdresi.Oku(context));

                // ⚠️ Cevap zaten başlamışsa gövdeye yazamayız —
                // "Headers are read-only" istisnası fırlar ve asıl hata
                // onun altında kaybolurdu. Bu durumda bağlantıyı olduğu
                // gibi bırakmak tek doğru davranış.
                if (context.Response.HasStarted)
                {
                    return;
                }

                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";

                // Geliştirmede gerçek hatayı görelim, canlıda GİZLEYELİM
                // (hata detayı sızdırmak güvenlik açığıdır)
                var mesaj = _env.IsDevelopment()
                    ? ex.Message
                    : "Sunucuda beklenmeyen bir hata oluştu.";

                await context.Response.WriteAsJsonAsync(new { mesaj = mesaj });
            }
        }

        // Token okunabildiyse kullanıcı kimliği; kimliksiz isteklerde null.
        private static int? KullaniciId(HttpContext context)
        {
            var talep = context.User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier);

            return talep != null && int.TryParse(talep.Value, out var id) ? id : null;
        }
    }
}
