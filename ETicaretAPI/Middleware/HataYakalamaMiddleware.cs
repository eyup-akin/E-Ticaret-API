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

        public HataYakalamaMiddleware(
            RequestDelegate next,
            ILogger<HataYakalamaMiddleware> logger,
            IHostEnvironment env)
        {
            _next = next;
            _logger = logger;
            _env = env;
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
    }
}