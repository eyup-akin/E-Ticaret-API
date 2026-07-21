using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;          // ⭐ YENİ — BadRequestObjectResult için
using ETicaretAPI.Data;
using ETicaretAPI.Middleware;            // ⭐ YENİ

using Hangfire;
using ETicaretAPI.Support;

var builder = WebApplication.CreateBuilder(args);

// EF Core'u SQL Server'a bağla
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// CORS: mobil ve web admin'in bağlanabilmesi için
builder.Services.AddCors(options =>
{
    options.AddPolicy("VarsayilanCors", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Geliştirmede serbest — admin panel farklı portlardan açılıyor,
            // mobil cihaz IP'si değişiyor; uğraştırmasın.
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            // Canlıda YALNIZCA bilinen origin'ler (admin panel domaini).
            // Liste appsettings > Cors:AllowedOrigins'ten okunur.
            var izinliOriginler = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? Array.Empty<string>();

            policy.WithOrigins(izinliOriginler)
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

// Token üreten servisi tanıt
builder.Services.AddScoped<ETicaretAPI.Services.TokenService>();


// ⭐ YENİ — email göndericisi. Şimdilik dev (konsola basan) uygulamayı bağladık.
// Canlıda bu satırı gerçek göndericiyle değiştireceğiz; başka hiçbir yer değişmeyecek.
builder.Services.AddScoped<ETicaretAPI.Services.IEmailGonderici, ETicaretAPI.Services.KonsolEmailGonderici>();



// ⭐ YENİ — dış URL'lerden resim indirebilmek için HttpClient fabrikası
builder.Services.AddHttpClient();


// ⭐ YENİ — resim indirmeye ÖZEL client: otomatik redirect KAPALI.
// SSRF: saldırgan public bir URL verip 302 ile iç adrese yönlendiremesin.
builder.Services.AddHttpClient("resimIndirici")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false
    });


// JWT token doğrulamayı kur
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true, // ⭐ DEĞİŞTİ — audience'ı da doğrula
        ValidAudience = builder.Configuration["Jwt:Audience"], // ⭐ YENİ
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
    };
});


// ⭐ YENİ — VALIDATION HATALARINI TEK TİPE ÇEVİR
// [ApiController] normalde şöyle döndürür:
//    { "errors": { "Price": ["Fiyat 0'dan büyük olmalı!"] }, "title": "...", "status": 400 }
// Biz onu ezip şuna çeviriyoruz:
//    { "mesaj": "Fiyat 0'dan büyük olmalı!" }
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            // Tüm validation mesajlarını topla
            var mesajlar = context.ModelState
                .Where(x => x.Value != null && x.Value.Errors.Count > 0)
                .SelectMany(x => x.Value!.Errors)
                .Select(e => e.ErrorMessage)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();

            var mesaj = mesajlar.Count > 0
                ? string.Join(" ", mesajlar)
                : "Gönderilen veri geçersiz.";

            return new BadRequestObjectResult(new { mesaj = mesaj });
        };
    });



// ⭐ YENİ — HANGFIRE KURULUMU
// İşleri aynı SQL Server veritabanında saklıyoruz (kalıcılık: sunucu
// restart olsa bile işler kaybolmaz).
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection")));

// Arka plan işçisini (server) başlat — işleri bu çeker ve çalıştırır
builder.Services.AddHangfireServer();

// İçe aktarma servisimizi tanıt (Hangfire bunu kendi scope'unda üretecek)
builder.Services.AddScoped<ETicaretAPI.Services.IceAktarmaServisi>();


// ⭐ YENİ — RATE LIMIT (brute-force / çok sık deneme koruması)
// "giris" politikası: bir IP, dakikada en fazla 5 login denemesi yapabilir.
// FixedWindow = sabit pencere: her 1 dakikalık dilimde sayaç sıfırlanır.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("giris", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            // Sayacı IP'ye göre böl — her IP'nin kendi kotası olsun.
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,                    // 1 dakikada 5 deneme
                Window = TimeSpan.FromMinutes(1),   // pencere boyu
                QueueLimit = 0                       // fazlasını bekletme, direkt reddet
            }));

    // Limit aşılınca ne dönsün? Kendi { mesaj } formatımıza uyalım
    // (mobil/admin zaten veri.mesaj okuyor).
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"mesaj\":\"Çok fazla deneme yaptın biladerim, lütfen biraz bekle.\"}", token);
    };
});

builder.Services.AddOpenApi();

var app = builder.Build();


// ⭐ YENİ — HATA YAKALAYICI EN BAŞTA OLMALI
// Sırası önemli: en dışta durup içeride patlayan her şeyi yakalasın.
app.UseMiddleware<HataYakalamaMiddleware>();


if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Mobil HTTP ile bağlanabilsin diye kapalı
// app.UseHttpsRedirection();

// ⭐ YENİ — wwwroot içindeki dosyaları dışarıya aç
// Böylece http://localhost:5289/uploads/urunler/xxx.jpg çalışır
app.UseStaticFiles();

app.UseCors("VarsayilanCors");

app.UseAuthentication();  // önce: token'ı oku, kim olduğunu belirle

// app.UseMiddleware<GuvenlikDamgasiMiddleware>(); //token bayat mı? //her istekte çalışması artık gereksiz. piplinedan çıkarıoyruz.
//kullanıcı pasif mi? silinmiş mi?

app.UseAuthorization();   // sonra: yetkisi var mı kontrol et

app.UseRateLimiter();     // ⭐ YENİ — rate limit kontrolünü devreye al



// ⭐ YENİ — Hangfire yönetim paneli (sadece localhost'tan erişilebilir)
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireYetkiFiltresi() }
});



app.MapControllers();

app.Run();