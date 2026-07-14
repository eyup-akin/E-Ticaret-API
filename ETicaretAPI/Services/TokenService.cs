using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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

        public string TokenUret(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),

                // ⭐ GÜVENLİK DAMGASI token'ın içine yazılıyor.
                // Her istekte veritabanındaki damga ile karşılaştırılacak.
                new Claim("stamp", user.SecurityStamp)
            };

            // ⭐ ROL HİYERARŞİSİ
            // Süper admin'e HEM "superadmin" HEM "admin" rolü veriyoruz.
            // Neden? Mevcut onlarca endpoint'te [Authorize(Roles = "admin")] yazıyor.
            // İki rolü birden verirsek, süper admin oralardan da geçer —
            // tek bir controller'a bile dokunmamıza gerek kalmaz.
            // Sadece insan yönetimi endpoint'lerine [Authorize(Roles = "superadmin")] koyacağız.
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
                expires: DateTime.UtcNow.AddDays(7),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}