using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;

namespace ETicaretAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ETicaretAPI.Services.TokenService _tokenService;

        public AuthController(AppDbContext context, ETicaretAPI.Services.TokenService tokenService)
        {
            _context = context;
            _tokenService = tokenService;
        }

        // POST /api/auth/register
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            // 1) Bu email daha önce kullanılmış mı kontrol et
            var emailVarMi = await _context.Users
                .AnyAsync(u => u.Email == dto.Email);

            if (emailVarMi)
            {
                return BadRequest(new { mesaj = "Bu email zaten kayıtlı biladerim!" });
            }

            // 2) Şifreyi hash'le (asla düz metin saklamıyoruz)
            var hashlenmisSifre = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            // 3) Yeni kullanıcıyı oluştur
            var yeniKullanici = new User
            {
                FullName = dto.FullName,
                Email = dto.Email,
                PasswordHash = hashlenmisSifre,
                Role = "customer" // yeni kayıtlar her zaman müşteri
            };

            // 4) Veritabanına ekle ve kaydet
            _context.Users.Add(yeniKullanici);
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Kayıt başarılı biladerim!" });
        }

        // POST /api/auth/login
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            // 1) Email'e göre kullanıcıyı bul
            var kullanici = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == dto.Email);

            // 2) Kullanıcı yoksa VEYA şifre yanlışsa — aynı mesajı ver (güvenlik)
            if (kullanici == null ||
                !BCrypt.Net.BCrypt.Verify(dto.Password, kullanici.PasswordHash))
            {
                return Unauthorized(new { mesaj = "Email veya şifre hatalı biladerim!" });
            }

            // 3) Doğruysa token üret
            var token = _tokenService.TokenUret(kullanici);

            // 4) Token'ı ve kullanıcı bilgisini döndür
            return Ok(new
            {
                token = token,
                fullName = kullanici.FullName,
                role = kullanici.Role
            });
        }



        // TEST: sadece giriş yapmış kullanıcı erişebilir
        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpGet("ben-kimim")]
        public IActionResult BenKimim()
        {
            // token'dan okunan bilgiler
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            var rol = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            return Ok(new { email, rol });
        }


    }
}