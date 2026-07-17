using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Hangfire;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.Services;

namespace ETicaretAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "admin")]   // tüm endpoint'ler admin'e özel
    public class ImportsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ImportsController(AppDbContext context)
        {
            _context = context;
        }

        // 🔴 POST /api/imports/test
        // 7a testi: gerçek Excel olmadan boş bir iş oluşturup Hangfire'a atar.
        [HttpPost("test")]
        public async Task<IActionResult> TestIsBaslat()
        {
            // 1) İş kaydını oluştur (Bekliyor)
            var job = new ImportJob
            {
                FileName = "test-isi.xlsx",
                Status = "Bekliyor",
                CreatedByUserId = KullaniciId()
            };

            _context.ImportJobs.Add(job);
            await _context.SaveChangesAsync();

            // 2) Hangfire kuyruğuna at — arka planda çalışacak.
            //    Burada iş HEMEN çalışmaz, kuyruğa girer; Hangfire çeker.
            BackgroundJob.Enqueue<IceAktarmaServisi>(s => s.TestIsiCalistir(job.Id));

            // 3) HEMEN cevap dön (202 Accepted): kullanıcı beklemesin
            return Accepted(new
            {
                jobId = job.Id,
                mesaj = "İş kabul edildi, arka planda çalışıyor."
            });
        }

        // 🔴 GET /api/imports/5  → bir işin son durumu
        [HttpGet("{id}")]
        public async Task<IActionResult> Durum(int id)
        {
            var job = await _context.ImportJobs.FindAsync(id);
            if (job == null)
            {
                return NotFound(new { mesaj = "İş bulunamadı." });
            }

            return Ok(new
            {
                id = job.Id,
                fileName = job.FileName,
                status = job.Status,
                total = job.Total,
                success = job.Success,
                failed = job.Failed,
                errorMessage = job.ErrorMessage,
                createdAt = job.CreatedAt,
                completedAt = job.CompletedAt
            });
        }

        // JWT'den kullanıcı id'sini çeker (yoksa null)
        private int? KullaniciId()
        {
            var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);

            if (claim != null && int.TryParse(claim.Value, out var id))
            {
                return id;
            }
            return null;
        }
    }
}