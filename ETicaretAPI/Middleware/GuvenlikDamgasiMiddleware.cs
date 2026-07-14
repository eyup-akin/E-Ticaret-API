using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;

namespace ETicaretAPI.Middleware
{
    // ⭐ TOKEN BAYATLAMASINI ÇÖZEN KATMAN
    //
    // PROBLEM:
    //   JWT durumsuzdur — içindeki bilgi, token üretildiği ANIN fotoğrafıdır.
    //   Bir admin'in rolünü düşürsen bile elindeki eski token'da hâlâ
    //   "role: admin" yazar. Backend token'a bakıp içeri alır. 💀
    //
    // ÇÖZÜM:
    //   Her kullanıcının bir "güvenlik damgası" (Guid) var. Token'a yazılıyor.
    //   Her istekte veritabanındaki damgayla karşılaştırıyoruz.
    //   Rol değişince damgayı yeniliyoruz → eski token'lar tutmuyor.
    //
    // ⚠️ ÖNEMLİ DAVRANIŞ KARARI:
    //   Bayat token gelince isteği REDDETMİYORUZ. Bunun yerine kullanıcıyı
    //   "MİSAFİR"E DÜŞÜRÜYORUZ (kimliği siliyoruz).
    //
    //   Neden? Ürün listesi gibi herkese açık endpoint'ler token OLMADAN da
    //   çalışır. Bayat token yüzünden onları da kapatırsak, uygulamayı açan
    //   kullanıcı bomboş bir ekran görür — misafir gezinme bile ölür.
    //
    //   Bu şekilde:
    //     - Açık endpoint'ler (ürünler, kategoriler) → çalışmaya devam eder
    //     - Korumalı endpoint'ler ([Authorize]) → 401 döner (kimlik yok çünkü)
    //   İstemci 401'i görünce token'ı silip kullanıcıyı giriş ekranına atar.
    public class GuvenlikDamgasiMiddleware
    {
        private readonly RequestDelegate _next;

        public GuvenlikDamgasiMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, AppDbContext db)
        {
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                var idMetni = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var tokenDamgasi = context.User.FindFirst("stamp")?.Value;

                if (int.TryParse(idMetni, out int userId))
                {
                    var kullanici = await db.Users
                        .AsNoTracking()
                        .Where(u => u.Id == userId)
                        .Select(u => new { u.SecurityStamp, u.IsActive })
                        .FirstOrDefaultAsync();

                    var gecersiz =
                        kullanici == null ||                       // kullanıcı yok
                        !kullanici.IsActive ||                     // pasifleştirilmiş
                        kullanici.SecurityStamp != tokenDamgasi;   // damga bayat

                    if (gecersiz)
                    {
                        // ⭐ İsteği KESMİYORUZ — kimliği siliyoruz.
                        // Bundan sonra bu istek "misafir" gibi işlenir.
                        context.User = new ClaimsPrincipal(new ClaimsIdentity());
                    }
                }
            }

            await _next(context);
        }
    }
}