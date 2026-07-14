using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;          // ⭐ YENİ — BadRequestObjectResult için
using ETicaretAPI.Data;
using ETicaretAPI.Middleware;            // ⭐ YENİ

var builder = WebApplication.CreateBuilder(args);

// EF Core'u SQL Server'a bağla
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// CORS: mobil ve web admin'in bağlanabilmesi için
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Token üreten servisi tanıt
builder.Services.AddScoped<ETicaretAPI.Services.TokenService>();

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
        ValidateAudience = false,
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

app.UseCors("AllowAll");

app.UseAuthentication();  // önce: token'ı oku, kim olduğunu belirle

app.UseMiddleware<GuvenlikDamgasiMiddleware>(); //token bayat mı?
//kullanıcı pasif mi? silinmiş mi?

app.UseAuthorization();   // sonra: yetkisi var mı kontrol et

app.MapControllers();

app.Run();