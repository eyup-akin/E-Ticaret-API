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

        // Bir kullanıcı için JWT token üretir
        public string TokenUret(User user)
        {
            // 1) Token'ın içine koyacağımız bilgiler (claims) — kim + hangi rol
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role) // rol burada! yetki kontrolü için
            };

            // 2) Gizli anahtarı al ve imza oluştur
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // 3) Token'ı oluştur (7 gün geçerli)
            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                claims: claims,
                expires: DateTime.UtcNow.AddDays(7),
                signingCredentials: creds
            );

            // 4) Token'ı metne çevir ve döndür
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}