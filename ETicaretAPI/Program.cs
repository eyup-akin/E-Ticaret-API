using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;


var builder = WebApplication.CreateBuilder(args);

// EF Core'u SQL Server'a bağla (appsettings.json'daki bağlantıyı kullanır)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));


// CORS: mobil ve web admin'in bağlanabilmesi için
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        // Geliştirme aşamasında her yere izin (sonra kısıtlanabilir)
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Token üreten servisi tanıt
builder.Services.AddScoped<ETicaretAPI.Services.TokenService>();


// JWT token doğrulamayı kur — gelen token'ları okur ve kontrol eder
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
        ValidateLifetime = true, // süresi dolmuş token'ı reddet
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
    };
});


// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();





// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

//biladerim bunu dev sırasında sorun çıkarmasın diye kapatıyoruz
//app.UseHttpsRedirection();

// CORS politikasını devreye al — SIRASI ÖNEMLİ (UseAuthorization'dan ÖNCE)
app.UseCors("AllowAll");

app.UseAuthentication();  // önce: token'ı oku, kim olduğunu belirle
app.UseAuthorization();   // sonra: yetkisi var mı kontrol et

app.MapControllers();

app.Run();
