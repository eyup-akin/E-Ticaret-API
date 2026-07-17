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
    [Authorize(Roles = "admin")]
    public class ImportsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        private const long MaxDosyaBoyutu = 10 * 1024 * 1024; // 10 MB

        public ImportsController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // 🔴 POST /api/imports/products   (multipart/form-data, alan adı: dosya)
        [HttpPost("products")]
        public async Task<IActionResult> UrunleriIceAktar([FromForm] IFormFile dosya)
        {
            if (dosya == null || dosya.Length == 0)
            {
                return BadRequest(new { mesaj = "Dosya seçilmedi!" });
            }

            if (dosya.Length > MaxDosyaBoyutu)
            {
                return BadRequest(new { mesaj = "Dosya en fazla 10 MB olabilir!" });
            }

            var uzanti = Path.GetExtension(dosya.FileName).ToLowerInvariant();
            if (uzanti != ".xlsx")
            {
                return BadRequest(new { mesaj = "Sadece .xlsx dosyası yükleyebilirsin." });
            }

            // 1) Dosyayı diske kaydet — arka plan işi buradan okuyacak.
            //    (Yüklenen dosyayı doğrudan Hangfire'a veremeyiz; istek biter,
            //     dosya kaybolur. O yüzden önce diske alıyoruz, yolunu iletiyoruz.)
            var klasor = Path.Combine(WebKok(), "uploads", "imports");
            Directory.CreateDirectory(klasor);

            var kayitAdi = Guid.NewGuid().ToString("N") + ".xlsx";
            var tamYol = Path.Combine(klasor, kayitAdi);

            using (var akis = new FileStream(tamYol, FileMode.Create))
            {
                await dosya.CopyToAsync(akis);
            }

            // 2) İş kaydını oluştur
            var job = new ImportJob
            {
                FileName = dosya.FileName,
                Status = "Bekliyor",
                CreatedByUserId = KullaniciId()
            };

            _context.ImportJobs.Add(job);
            await _context.SaveChangesAsync();

            // 3) Hangfire kuyruğuna at
            BackgroundJob.Enqueue<IceAktarmaServisi>(s => s.UrunleriIceAktar(job.Id, tamYol));

            // 4) Hemen 202 dön — kullanıcı beklemesin
            return Accepted(new
            {
                jobId = job.Id,
                mesaj = "Dosya alındı, ürünler arka planda ekleniyor."
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

        private string WebKok()
        {
            return string.IsNullOrEmpty(_env.WebRootPath)
                ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
                : _env.WebRootPath;
        }

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
