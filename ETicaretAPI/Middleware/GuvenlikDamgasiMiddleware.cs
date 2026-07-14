using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;

namespace ETicaretAPI.Middleware
{
    // ⭐ TOKEN BAYATLAMASINI ÇÖZEN KATMAN
    //
    // PROBLEM:
    //   JWT "durumsuzdur" (stateless) — içindeki bilgi token üretildiği ANIN fotoğrafıdır.
    //   Bir admin'in rolünü müşteriye düşürsen bile, elindeki eski token'da
    //   hâlâ "role: admin" yazar. Backend token'a bakıp içeri alır. 💀
    //
    // ÇÖZÜM:
    //   Her kullanıcının bir "güvenlik damgası" (Guid) var. Token üretilirken
    //   içine yazılıyor. Her istekte veritabanındaki damgayla karşılaştırıyoruz.
    //   Rol değişince damgayı YENİLİYORUZ → eski token'ların damgası tutmuyor → 401.
    //
    // BEDELİ:
    //   Korumalı her istekte 1 ekstra veritabanı sorgusu. JWT'nin "durumsuzluk"
    //   avantajından kısmen vazgeçiyoruz. Gerçek sistemlerde bu damga Redis gibi
    //   bir önbellekte tutulur, DB'ye gidilmez. Bizim ölçeğimizde sorgu yeterli.
    public class GuvenlikDamgasiMiddleware
    {
        private readonly RequestDelegate _next;

        public GuvenlikDamgasiMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        // AppDbContext parametre olarak isteniyor — her istek için taze bir tane gelir
        public async Task InvokeAsync(HttpContext context, AppDbContext db)
        {
            // Sadece token'la gelen isteklerle ilgileniyoruz.
            // Herkese açık endpoint'ler (ürün listesi vb.) buradan hızlıca geçer.
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
                        kullanici == null ||                          // kullanıcı silinmiş
                        !kullanici.IsActive ||                        // pasifleştirilmiş
                        kullanici.SecurityStamp != tokenDamgasi;      // damga bayatlamış

                    if (gecersiz)
                    {
                        context.Response.StatusCode = 401;
                        context.Response.ContentType = "application/json";

                        await context.Response.WriteAsJsonAsync(new
                        {
                            mesaj = "Oturumun geçersiz hale geldi. Lütfen tekrar giriş yap."
                        });

                        return; // isteği burada kes, controller'a gitmesin
                    }
                }
            }

            await _next(context);
        }
    }
}