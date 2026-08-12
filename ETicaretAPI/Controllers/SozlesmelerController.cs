using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;

namespace ETicaretAPI.Controllers
{
    // ⭐ YENİ (Aşama 10) — SÖZLEŞME METİNLERİ
    //
    // Herkese açık: onay kutusunun yanındaki metne kayıt olmadan da
    // bakılabilmeli.
    [Route("api/[controller]")]
    [ApiController]
    public class SozlesmelerController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SozlesmelerController(AppDbContext context)
        {
            _context = context;
        }

        // 🟢 GET /api/sozlesmeler — aktif metinlerin listesi (içeriksiz)
        [HttpGet]
        public async Task<IActionResult> Liste()
        {
            // ⚠️ İçerik gönderilmiyor: liste ucu özet, detay ucu tam veri.
            var liste = await _context.Sozlesmeler
                .Where(s => s.AktifMi)
                .OrderBy(s => s.Tip)
                .Select(s => new { s.Id, s.Tip, s.Surum, s.YayinTarihi })
                .ToListAsync();

            return Ok(liste);
        }

        // 🟢 GET /api/sozlesmeler/gizlilik — aktif sürümün tam metni
        [HttpGet("{tip}")]
        public async Task<IActionResult> Getir(string tip)
        {
            if (!SozlesmeTipi.Gecerliler.Contains(tip))
            {
                return NotFound(new { mesaj = "Böyle bir sözleşme yok." });
            }

            var sozlesme = await _context.Sozlesmeler
                .Where(s => s.Tip == tip && s.AktifMi)
                .Select(s => new { s.Id, s.Tip, s.Surum, s.Icerik, s.YayinTarihi })
                .FirstOrDefaultAsync();

            if (sozlesme == null)
            {
                return NotFound(new { mesaj = "Bu sözleşmenin yayında bir sürümü yok." });
            }

            return Ok(sozlesme);
        }
    }
}
