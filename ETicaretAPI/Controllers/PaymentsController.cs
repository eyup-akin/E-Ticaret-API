using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ETicaretAPI.Data;

namespace ETicaretAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PaymentsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public PaymentsController(AppDbContext context)
        {
            _context = context;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // 🟡 GET /api/payments — kendi ödeme geçmişim
        [HttpGet]
        public async Task<IActionResult> GetMyPayments()
        {
            var userId = GetUserId();

            var payments = await _context.Payments
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.Id)
                .Select(p => new
                {
                    p.Id,
                    p.OrderId,
                    p.Amount,
                    p.CardLast4,
                    p.Status,
                    p.PaidAt
                })
                .ToListAsync();

            return Ok(payments);
        }

        // 🔴 GET /api/admin/payments — tüm ödemeler (admin)
        [Authorize(Roles = "admin")]
        [HttpGet("/api/admin/payments")]
        public async Task<IActionResult> GetAllPayments()
        {
            var payments = await _context.Payments
                .OrderByDescending(p => p.Id)
                .Select(p => new
                {
                    p.Id,
                    p.OrderId,
                    p.UserId,
                    p.Amount,
                    p.CardLast4,
                    p.Status,
                    p.PaidAt
                })
                .ToListAsync();

            return Ok(payments);
        }

        // 🔴 GET /api/admin/revenue — toplam gelir özeti (admin)
        [Authorize(Roles = "admin")]
        [HttpGet("/api/admin/revenue")]
        public async Task<IActionResult> GetRevenue()
        {
            // Sadece başarılı ödemeleri say
            var basariliOdemeler = _context.Payments.Where(p => p.Status == "basarili");

            var toplamGelir = await basariliOdemeler.SumAsync(p => (decimal?)p.Amount) ?? 0;
            var odemeSayisi = await basariliOdemeler.CountAsync();

            return Ok(new
            {
                toplamGelir = toplamGelir,
                odemeSayisi = odemeSayisi
            });
        }


    }
}