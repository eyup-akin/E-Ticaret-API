using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;

namespace ETicaretAPI.Controllers
{
    [Route("api/admin")]
    [ApiController]
    [Authorize(Roles = "admin")] // TÜM admin controller'ı sadece admin'e açık
    public class AdminController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AdminController(AppDbContext context)
        {
            _context = context;
        }

        // 🔴 GET /api/admin/users — tüm müşteriler
        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _context.Users
                .Select(u => new
                {
                    u.Id,
                    u.FullName,
                    u.Email,
                    u.Role
                    // PasswordHash ASLA gönderilmiyor — güvenlik
                })
                .ToListAsync();

            return Ok(users);
        }

        // 🔴 GET /api/admin/dashboard — özet istatistikler
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var toplamSiparis = await _context.Orders.CountAsync();
            var toplamUrun = await _context.Products.CountAsync();
            var toplamMusteri = await _context.Users.CountAsync(u => u.Role == "customer");
            var toplamGelir = await _context.Payments
                .Where(p => p.Status == "basarili")
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            return Ok(new
            {
                toplamSiparis = toplamSiparis,
                toplamUrun = toplamUrun,
                toplamMusteri = toplamMusteri,
                toplamGelir = toplamGelir
            });
        }
    }
}