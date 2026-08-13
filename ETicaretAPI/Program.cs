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

// ⭐ YENİ — denetim kaydı yazıcısı.
//
// Scoped: DbContext'e bağlı, onunla aynı ömre sahip olmalı.
// (StokDefteri ile aynı gerekçe; ikisi de "context'e ekle, kaydetme"
//  deseninde çalışıyor.)
builder.Services.AddScoped<ETicaretAPI.Services.DenetimKaydi>();

// ⭐ YENİ (Aşama 8) — destek yazışmasını okuyan ortak servis.
//
// Scoped: DbContext'e bağımlı, yani onunla aynı ömre sahip olmalı.
// Singleton yapsaydık ilk isteğin DbContext'ini sonsuza kadar
// tutardı.
builder.Services.AddScoped<ETicaretAPI.Services.DestekYazismasi>();

// ⭐ YENİ (Aşama 9) — iade tutarını hesaplayan servis.
//
// Singleton: durumu yok, DbContext'e bağlı değil, saf hesap.
// (SepetHesaplayici ve KdvHesaplayici ile aynı ömür.)
builder.Services.AddSingleton<ETicaretAPI.Services.IadeHesaplayici>();

// ⭐ YENİ (Aşama 10) — sözleşme onaylarını yazan servis (DbContext'e bağlı).
builder.Services.AddScoped<ETicaretAPI.Services.SozlesmeOnayServisi>();

// ⭐ YENİ — kombin önerileri ve kombin indirimi (DbContext'e bağlı).
builder.Services.AddScoped<ETicaretAPI.Services.KombinServisi>();

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

// ⭐ YENİ — sepet/kargo hesabı.
//
// Singleton: durumu yok, sadece MagazaAyarlari'na bakıyor
// (o da singleton). DbContext almadığı için güvenle paylaşılabilir.
builder.Services.AddSingleton<ETicaretAPI.Services.SepetHesaplayici>();

// ⭐ YENİ — KDV ayrıştırma.
//
// Singleton ve SepetHesaplayici'den bile "daha saf": ayarlara bile
// bakmıyor, oranı çağıran veriyor. Tamamen girdi-çıktı bir sınıf.
//
// ⚠️ Bu servis TOPLAM ÜRETMEZ. Fiyatlar KDV dahil olduğu için toplam
// değişmiyor; burada üretilen sadece o toplamın dökümü. Ödenecek
// tutarın tek kaynağı SepetHesaplayici'dir ve öyle kalmalı.
builder.Services.AddSingleton<ETicaretAPI.Services.KdvHesaplayici>();

// ⭐ YENİ — sepete ekleme kuralı (upsert + adet üst sınırı).
//
// ⚠️ Scoped, singleton DEĞİL: DbContext alıyor. Singleton yapmak
// aynı context'i tüm isteklerde paylaşmak olurdu.
builder.Services.AddScoped<ETicaretAPI.Services.SepetEkleyici>();


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

// ⭐ YENİ (5.5) — stok bildirim servisi (Hangfire kendi scope'unda üretecek)
builder.Services.AddScoped<ETicaretAPI.Services.StokBildirimServisi>();


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



    // ⭐ YENİ (Aşama 8) — "destek" politikası: talep açma ucu için.
    //
    // NEDEN GEREKLİ?
    // Talep açmak yazma işlemi ve spam'e açık: bir betik dakikada
    // yüzlerce talep açıp hem veritabanını hem admin ekranını
    // kullanılamaz hale getirebilir. "Rate limit sadece giriş için
    // değildir."
    //
    // ⚠️ SAYAÇ KULLANICIYA GÖRE BÖLÜNÜYOR, IP'YE GÖRE DEĞİL.
    // Uç [Authorize] altında, yani kimliği BİLİYORUZ. IP'ye göre
    // saysaydık aynı ofisten/kafeden çıkan herkes tek IP görünür ve
    // biri kotayı doldurunca masum olanlar da cezalanırdı.
    // "Sayacı en dar kimliğe göre böl."
    //
    // 10/saat: gerçek bir müşteri günde bir-iki talep açar; on tanesi
    // fazlasıyla geniş. Yazışma (mesaj ekleme) bu limite GİRMİYOR —
    // sınırlanan yeni talep açmak, konuşmak değil. Konuşmayı
    // kısıtlamak, sorunu çözmeye çalışan müşteriyi cezalandırırdı.
    options.AddPolicy("destek", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirst(
                              System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "bilinmeyen",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromHours(1),
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


// ⭐ YENİ — VEKİL ARKASINDA GERÇEK İSTEMCİ IP'Sİ
//
// Vekil (Caddy) arkasına geçince Kestrel'in gördüğü adres vekilin
// adresi olur ve IP'ye göre bölünen rate limit sayaçları herkesi TEK
// kovaya toplar: bir kişi giriş limitini doldurunca hepsi 429 yer.
//
// ⚠️ Liste BOŞSA middleware hiç eklenmiyor — X-Forwarded-For'a körü
// körüne güvenmek istemciye "IP'mi ben söylerim" demek olurdu.
// appsettings'te boş, yalnızca compose dolduruyor.
var guvenilirVekilAglari = builder.Configuration["Vekil:GuvenilirAglar"];
if (!string.IsNullOrWhiteSpace(guvenilirVekilAglari))
{
    var vekilSecenekleri = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                         | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,

        // ⚠️ 1 kalmalı: yalnızca EN SAĞDAKİ kayıt okunur, o da vekilin
        // kendi eklediğidir. Artırmak istemcinin yazdığı sahte
        // kayıtları da kabul etmek olurdu.
        ForwardLimit = 1
    };

    // Varsayılan liste sadece loopback tanıyor; temizleyip kendi
    // ağlarımızı koyuyoruz.
    vekilSecenekleri.KnownNetworks.Clear();
    vekilSecenekleri.KnownProxies.Clear();

    foreach (var cidr in guvenilirVekilAglari.Split(
                 ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        // ⚠️ İki ayrı IPNetwork tipi var. System.Net olan CIDR metnini
        // ayrıştırıyor, HttpOverrides olan ise seçeneklerin beklediği tip.
        var ag = System.Net.IPNetwork.Parse(cidr);
        vekilSecenekleri.KnownNetworks.Add(
            new Microsoft.AspNetCore.HttpOverrides.IPNetwork(ag.BaseAddress, ag.PrefixLength));
    }

    app.UseForwardedHeaders(vekilSecenekleri);
}


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

// ⭐ GERİ AÇILDI — token bayat mı? kullanıcı pasif mi?
//
// ⚠️ BU SATIR BİR SÜRE YORUMDA KALDI VE SESSİZ BİR AÇIK ÜRETTİ.
//
// Kapalıyken JWT'nin tek güvencesi imza ve süreydi. Sonuç: acil yetki
// iptali diye bir şey kalmamıştı. Access token 15 dakikalık olduğu için
// aşağıdaki dört işlemin hiçbiri ANINDA etki etmiyordu:
//
//   • Süperadmin bir admin'i müşteriye düşürür → elindeki token 15 dk
//     daha ADMİN yetkisiyle çalışırdı
//   • Hesap pasifleştirilir            → 15 dk daha her uca erişirdi
//   • Şifre değiştirilir / sıfırlanır  → çalınan token 15 dk daha geçerliydi
//   • Hesap kapatılır (KVKK)           → anonimleştirilmiş hesabın token'ı
//                                        15 dk daha çalışırdı
//
// Refresh token'lar bu işlemlerde iptal ediliyordu, evet — ama access
// token refresh yolundan geçmiyor. Yani iptal bir sonraki yenilemeye
// kadar bekliyordu.
//
// Daha kötüsü: AuthController ve AdminController'daki yorumlar
// "eldeki tüm access token'lar ANINDA geçersizleşir" diyordu. Kodu
// okuyan "damgayı yeniledim, iş bitti" sanıyordu. Bayat yorum,
// yorumsuz koddan kötüdür.
//
// ⚠️ MALİYETİ: istek başına bir SELECT — birincil anahtar üzerinden,
// tek satır, iki kolon (AsNoTracking). Bu ölçekte ölçülemez.
// Önbellek (IMemoryCache) BİLEREK eklenmedi: kazancı yok, karşılığında
// bir invalidation mantığı ve onunla birlikte yeni bir hata kaynağı
// getirirdi. Veri büyürse o zaman değerlendirilir.
//
// ⚠️ SIRASI ÖNEMLİ: UseAuthentication'dan SONRA (kimlik okunmuş olmalı),
// UseAuthorization'dan ÖNCE (yetki kontrolünden önce kimliği düşürelim).
app.UseMiddleware<GuvenlikDamgasiMiddleware>();

app.UseAuthorization();   // sonra: yetkisi var mı kontrol et

app.UseRateLimiter();     // ⭐ YENİ — rate limit kontrolünü devreye al



// ⭐ YENİ — Hangfire yönetim paneli (sadece localhost'tan erişilebilir)
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireYetkiFiltresi() }
});

// ⭐ YENİ (5.5) — STOK BİLDİRİMİ TARAMASI
//
// Bekleyen "stoka gelince haber ver" isteklerini tarar ve ürünü
// tekrar satılabilir hale gelmiş olanlara mail atar.
//
// ⚠️ NEDEN PERİYODİK, NEDEN STOK ARTIŞINDA TETİKLENMİYOR?
// Gerekçe StokBildirimServisi'nin başında uzun uzun yazılı: kısaca,
// StokDefteri bilerek SaveChanges çağırmadığı için "stok gerçekten
// arttı" anını göremiyor; alternatif stoğu artıran beş ayrı yere
// çağrı koymaktı ve orada unutulan bir yer, müşterinin hiç mail
// almaması demekti. Tek nokta, atlanamaz.
//
// ⚠️ SABİT İŞ KİMLİĞİ ("stok-bildirimleri") — AddOrUpdate bu adı
// anahtar olarak kullanıyor. Sabit olmasaydı her uygulama açılışında
// yeni bir tekrarlayan iş eklenir ve mailler kat kat çoğalırdı.
//
// Dakikada bir: "stoka geldi" bildirimi için birkaç dakikalık
// gecikme kabul edilebilir, üstelik tarama boş geçtiğinde tek bir
// hafif sorgu maliyeti var.
RecurringJob.AddOrUpdate<ETicaretAPI.Services.StokBildirimServisi>(
    "stok-bildirimleri",
    servis => servis.BekleyenleriGonderAsync(),
    "*/1 * * * *");



app.MapControllers();

app.Run();