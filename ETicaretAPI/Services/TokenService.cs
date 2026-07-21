using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;   // ⭐ YENİ — güvenli rastgelelik + SHA-256 için
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ETicaretAPI.Models;

namespace ETicaretAPI.Services
{
    public class TokenService
    {
        private readonly IConfiguration _config;

        public TokenService(IConfiguration config)
        {
            _config = config;
        }

        // ⭐ ACCESS TOKEN — kısa ömürlü (15 dk) kimlik kartı.
        // Metodun adı eskiden beri "TokenUret"; artık YALNIZCA access üretiyor.
        // (İsmi bilerek değiştirmedim ki AuthController'daki mevcut çağrı kırılmasın;
        //  1c'de orayı elden geçirirken bu isim aynen çalışmaya devam edecek.)
        public string TokenUret(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),

                // Güvenlik damgası hâlâ token'ın içinde duruyor — ileride
                // refresh anında bununla "bu token bayat mı?" kontrolü yapacağız.
                new Claim("stamp", user.SecurityStamp)
            };

            // Rol hiyerarşisi aynen korunuyor: superadmin hem "superadmin"
            // hem "admin" rolünü taşır, böylece admin endpoint'lerinden de geçer.
            claims.Add(new Claim(ClaimTypes.Role, user.Role));

            if (user.Role == "superadmin")
            {
                claims.Add(new Claim(ClaimTypes.Role, "admin"));
            }

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                claims: claims,

                // ⭐ DEĞİŞTİ: 7 gün → 15 dakika.
                // Kısa ömür = token çalınsa bile saldırganın elinde kalma
                // penceresi en fazla 15 dk olur. Kullanıcının oturumda KALMASINI
                // ise artık uzun ömürlü REFRESH token sağlayacak.
                expires: DateTime.UtcNow.AddMinutes(15),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // ⭐ YENİ — REFRESH TOKEN üretir.
        // Bu bir JWT DEĞİLDİR; sadece tahmin edilemeyen, rastgele bir metindir.
        //
        // NEDEN JWT DEĞİL?
        //   JWT durumsuzdur → tek başına iptal edilemez. Refresh'i iptal
        //   edebilmemiz şart (cihaz çıkışı, hırsızlık). O yüzden anlamsız ama
        //   rastgele bir metin üretip veritabanında yönetiyoruz.
        public string RefreshTokenUret()
        {
            // 64 baytlık KRİPTOGRAFİK olarak güvenli rastgelelik.
            //
            // NEDEN "Random" DEĞİL DE "RandomNumberGenerator"?
            //   Sıradan Random() tahmin edilebilir (bir tohuma dayanır) —
            //   güvenlik token'ında ASLA kullanılmaz. RandomNumberGenerator
            //   ise işletim sisteminin güvenli rastgelelik kaynağını kullanır,
            //   çıktısı tahmin edilemez.
            var baytlar = RandomNumberGenerator.GetBytes(64);

            // Baytları okunabilir metne çevirip istemciye veririz.
            // (İstek gövdesinde taşınacağı için standart Base64 sorun değil.)
            return Convert.ToBase64String(baytlar);
        }

        // ⭐ YENİ — verilen metnin SHA-256 hash'ini (hex) döndürür.
        //
        // Ham refresh token'ı veritabanına YAZMADAN önce bununla hash'leriz.
        // Doğrularken de kullanıcıdan gelen ham token'ı yine bununla hash'leyip
        // veritabanındaki hash ile karşılaştırırız. Böylece DB'de asla ham
        // token durmaz — sızsa bile içeriden giriş yapılamaz.
        public string Hashle(string hamMetin)
        {
            var baytlar = Encoding.UTF8.GetBytes(hamMetin);
            var hashBaytlari = SHA256.HashData(baytlar);

            // Baytları 64 karakterlik hex metne çevir (küçük harf).
            // (RefreshTokens tablosundaki TokenHash kolonu tam 64 karakterlik.)
            return Convert.ToHexString(hashBaytlari).ToLowerInvariant();
        }
    }
}