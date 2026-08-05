using ETicaretAPI.Data;
using ETicaretAPI.Middleware;            // ⭐ YENİ
using ETicaretAPI.Services;
using ETicaretAPI.Support;
using Hangfire;
using Microsoft.AspNetCore.Mvc;          // ⭐ YENİ — BadRequestObjectResult için
using Microsoft.EntityFrameworkCore;


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

// ⭐ YENİ — e-posta şablon üreticisi.
//
// Neden Scoped, neden Singleton değil?
// IConfiguration zaten Singleton ve sınıfın başka durumu yok, teknik
// olarak Singleton da olurdu. Scoped seçtik çünkü projedeki diğer
// servisler (KuponServisi, IceAktarmaServisi) da Scoped — tutarlılık
// ve ileride veritabanı erişimi eklemek gerekirse hazır olması için.
builder.Services.AddScoped<ETicaretAPI.Services.EmailSablonlari>();

// ⭐ YENİ — rapor tarih aralığı hesaplayıcısı.
//
// Neden AddScoped? Diğer servislerle aynı ömür — istek başına bir
// örnek. Aslında bu sınıf durum tutmadığı için Singleton da olabilirdi
// (bir kez üretilip hep kullanılırdı), ama projedeki desenden sapmamak
// okunabilirlik açısından daha değerli. Tek bir nesnenin ömrü için
// istisna kuralı yazmaya değmez.
builder.Services.AddScoped<ETicaretAPI.Services.RaporTarihi>();


// ⭐ YENİ — stok hareket defteri yazıcısı
builder.Services.AddScoped<ETicaretAPI.Services.StokDefteri>();

// ⭐ YENİ — mağaza ayarları.
//
// NEDEN Singleton, Scoped DEĞİL?
//
// Bu sınıfın DURUMU YOK — sadece IConfiguration'a bakıp değer
// döndürüyor. Her istek için yeni bir örnek üretmenin hiçbir
// faydası olmaz, sadece boşuna nesne oluşur.
//
// Kural: bağımlılıkları da singleton olabilen, durumsuz servisler
// singleton olur. IConfiguration zaten singleton, uyumlu.
//
// ⚠️ Karşılaştır: StokDefteri Scoped, çünkü AppDbContext alıyor
// ve DbContext isteğe özeldir. Bir singleton'ın DbContext alması
// ciddi bir hata olurdu (aynı context'i tüm isteklerde paylaşmak).
builder.Services.AddSingleton<ETicaretAPI.Services.MagazaAyarlari>();


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


builder.Services.AddControllers()
    // ⭐ YENİ — TÜM TARİHLER UTC OLARAK GİDİP GELSİN
    //
    // Neden burada? Çünkü API'nin dış dünyayla konuştuğu tek nokta
    // serileştirme katmanı. Buraya bir kere yazınca 14 controller'ın
    // tüm endpoint'leri düzeliyor — tek tek gezmeye gerek kalmıyor.
    //
    // Sıra önemli değil, ConfigureApiBehaviorOptions'tan önce ya da
    // sonra olabilir; ikisi farklı şeyleri yapılandırıyor.
    .AddJsonOptions(options =>
    {
    options.JsonSerializerOptions.Converters.Add(
        new UtcTarihDonusturucu());

    options.JsonSerializerOptions.Converters.Add(
        new UtcTarihDonusturucuNullable());
    })
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



    // ⭐ YENİ — "eposta" politikası: mail GÖNDEREN endpoint'ler için.
    // Neden ayrı ve daha sıkı? Mail göndermek pahalı bir işlem:
    //   - kurbanın gelen kutusu bombalanabilir (taciz)
    //   - canlıda mail başına para ödenir (financial DoS)
    //   - anormal gönderim mail servisini kara listeye düşürür
    // 15 dakikada 3 istek: gerçek kullanıcı için fazlasıyla yeterli,
    // saldırgan için işe yaramaz.
    options.AddPolicy("eposta", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0
            }));



    // ⭐ YENİ — "kupon" politikası: kupon kodu doğrulama ucu için.
    //
    // NEDEN GEREKLİ?
    // Kupon kodları kısa ve tahmin edilebilir (YAZ25, INDIRIM10...).
    // Sınırsız deneme hakkı olan biri sözlük saldırısıyla geçerli kod
    // bulabilir. "Tahmin edilebilir kısa kod" içeren HER uç, giriş
    // ekranı kadar brute-force'a açıktır.
    //
    // NEDEN "giris" POLİTİKASINI KULLANMADIK?
    //
    // 1) Bölümleme anahtarı farklı olmalı.
    //    Bu uç [Authorize] ile korunuyor — çağıranın KİM olduğunu
    //    biliyoruz. IP'ye göre saymak iki yönden yanlış olurdu:
    //      • Aynı NAT/kurumsal ağdan çıkan onlarca masum kullanıcı tek
    //        IP görünür, biri limiti doldurunca hepsi cezalanır.
    //      • Saldırgan VPN ile IP değiştirip sayacı sıfırlar.
    //    Kullanıcı id'sine göre bölünce limiti aşmanın tek yolu YENİ
    //    HESAP açmak; o da e-posta doğrulama ve kayıt limitine çarpar.
    //
    //    Kimlik yoksa IP'ye düşüyoruz — ama pratikte bu dal hiç
    //    çalışmamalı ([Authorize] zaten kimliksizi içeri almıyor).
    //    Yine de savunma amaçlı bırakıyoruz: politika ileride başka bir
    //    uçta kullanılırsa partitionKey'in null olması tüm kimliksiz
    //    istekleri TEK sayaca toplardı — istemediğimiz şey bu.
    //
    // 2) Sayı farklı olmalı.
    //    5/dakika kupon için çok dar. Mobil uygulama sepet her
    //    değiştiğinde kuponu yeniden doğruluyor (indirim tutarı
    //    bayatlamasın diye). Debounce koyduk ama yine de normal bir
    //    alışverişte dakikada birkaç istek olabiliyor.
    //    20/dakika: gerçek kullanıcı için asla dolmaz, saldırganı ise
    //    saatte 1200 denemeye hapseder.
    options.AddPolicy("kupon", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirst(
                              System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "bilinmeyen",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));



    // ⭐ YENİ — "basvuru" politikası: admin başvuru ucu için.
    //
    // NEDEN AYRI BİR POLİTİKA?
    //
    // Bu uç ŞİFRE DOĞRULUYOR — yani giriş ekranı kadar brute-force'a
    // açık. Limitsiz bırakırsak saldırgan buradan şifre deneyebilir
    // ve /login'deki 5/dakika limitini tamamen atlamış olur.
    // "Rate limit sadece giriş için değildir."
    //
    // NEDEN "giris" POLİTİKASINI KULLANMADIK?
    // Sayaç paylaşılırdı: başvuru yapan biri kendi giriş hakkını
    // yakardı ve sonra hesabına giremezdi.
    //
    // NEDEN IP'YE GÖRE BÖLÜNÜYOR?
    // Bu uç [Authorize] ALTINDA DEĞİL — çağıranın kim olduğunu
    // bilmiyoruz. Elimizdeki tek kimlik IP.
    //
    // 3/saat: gerçek bir başvuru sahibi ömründe bir kez kullanır.
    options.AddPolicy("basvuru", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                //PermitLimit = 3,
                // ⭐ DEĞİŞTİ: 3 → 5
                //
                // Neden gevşettik? Bu ucun brute-force değeri düşük:
                // cevap HER DURUMDA aynı, yani saldırgan denediği
                // şifrenin doğru olup olmadığını ANLAYAMIYOR.
                // Klasik giriş ekranından farkı bu — orada 200/401
                // farkı bilgi taşır, burada taşımaz.
                //
                // Geriye tek gerçek risk kalıyor: BCrypt.Verify
                // pahalı bir işlem, sınırsız istek CPU'yu yorar (DoS).
                // Onu 5/saat de engelliyor.
                //
                // ⚠️ Sayaç IP'ye göre bölünüyor ve bu ucun bir
                // zayıflığı: aynı ofisten/kafeden çıkan herkes tek
                // IP görünür, biri kotayı doldurunca hepsi cezalanır.
                // Alternatifi yok — uç [Authorize] altında olmadığı
                // için elimizdeki tek kimlik IP.
                PermitLimit = 5,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
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

builder.Services.AddScoped<KuponServisi>();


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