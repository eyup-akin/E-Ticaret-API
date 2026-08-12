using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;

namespace ETicaretAPI.Controllers
{
    // ⭐ YENİ — KOMBİN YÖNETİMİ (admin)
    [Route("api/admin/kombinler")]
    [ApiController]
    [Authorize(Roles = "admin")]
    public class AdminKombinlerController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AdminKombinlerController(AppDbContext context)
        {
            _context = context;
        }

        // 🔴 GET /api/admin/kombinler
        [HttpGet]
        public async Task<IActionResult> Liste()
        {
            var kombinler = await _context.Kombinler
                .OrderByDescending(k => k.Id)
                .Select(k => new
                {
                    k.Id,
                    k.Ad,
                    k.Aciklama,
                    k.IndirimYuzdesi,
                    k.AktifMi,
                    k.CreatedAt,

                    urunler = _context.KombinUrunler
                        .Where(ku => ku.KombinId == k.Id)
                        .Join(_context.Products,
                              ku => ku.ProductId,
                              p => p.Id,
                              (ku, p) => new { p.Id, p.Name, p.Price, p.IsActive })
                        .ToList()
                })
                .ToListAsync();

            return Ok(kombinler);
        }

        // 🔴 POST /api/admin/kombinler
        [HttpPost]
        public async Task<IActionResult> Ekle([FromBody] KombinKaydetDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var hata = await DogrulaAsync(dto);
            if (hata != null)
            {
                return BadRequest(new { mesaj = hata });
            }

            var kombin = new Kombin
            {
                Ad = dto.Ad.Trim(),
                Aciklama = string.IsNullOrWhiteSpace(dto.Aciklama) ? null : dto.Aciklama.Trim(),
                IndirimYuzdesi = dto.IndirimYuzdesi,
                AktifMi = dto.AktifMi,
                CreatedAt = DateTime.UtcNow
            };

            await using var tx = await _context.Database.BeginTransactionAsync();

            _context.Kombinler.Add(kombin);
            await _context.SaveChangesAsync();

            foreach (var urunId in dto.UrunIdleri.Distinct())
            {
                _context.KombinUrunler.Add(new KombinUrun
                {
                    KombinId = kombin.Id,
                    ProductId = urunId
                });
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new { mesaj = "Kombin oluşturuldu.", id = kombin.Id });
        }

        // 🔴 PUT /api/admin/kombinler/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Guncelle(int id, [FromBody] KombinKaydetDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var kombin = await _context.Kombinler.FirstOrDefaultAsync(k => k.Id == id);

            if (kombin == null)
            {
                return NotFound(new { mesaj = "Kombin bulunamadı!" });
            }

            var hata = await DogrulaAsync(dto);
            if (hata != null)
            {
                return BadRequest(new { mesaj = hata });
            }

            kombin.Ad = dto.Ad.Trim();
            kombin.Aciklama = string.IsNullOrWhiteSpace(dto.Aciklama) ? null : dto.Aciklama.Trim();
            kombin.IndirimYuzdesi = dto.IndirimYuzdesi;
            kombin.AktifMi = dto.AktifMi;

            await using var tx = await _context.Database.BeginTransactionAsync();

            // Kalemler tamamen yenileniyor: hangi ürünün eklenip
            // çıkarıldığını ayrı ayrı izlemek bu boyutta gereksiz.
            await _context.KombinUrunler
                .Where(ku => ku.KombinId == id)
                .ExecuteDeleteAsync();

            foreach (var urunId in dto.UrunIdleri.Distinct())
            {
                _context.KombinUrunler.Add(new KombinUrun
                {
                    KombinId = id,
                    ProductId = urunId
                });
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new { mesaj = "Kombin güncellendi." });
        }

        // 🔴 DELETE /api/admin/kombinler/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Sil(int id)
        {
            var kombin = await _context.Kombinler.FirstOrDefaultAsync(k => k.Id == id);

            if (kombin == null)
            {
                return NotFound(new { mesaj = "Kombin bulunamadı!" });
            }

            // Kalemler FK'da Cascade — birlikte gidiyor.
            // ⚠️ Kombin bir öneri, ticari kayıt değil: silinebilir.
            // Geçmiş siparişlerin indirimi zaten donmuş durumda.
            _context.Kombinler.Remove(kombin);
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Kombin silindi." });
        }

        private async Task<string?> DogrulaAsync(KombinKaydetDto dto)
        {
            var idler = dto.UrunIdleri.Distinct().ToList();

            // ⚠️ En az iki ürün: tek ürünlük "kombin" diye bir şey yok.
            if (idler.Count < 2)
            {
                return "Kombinde en az iki ürün olmalı.";
            }

            if (idler.Count > 5)
            {
                return "Kombinde en fazla beş ürün olabilir.";
            }

            var mevcut = await _context.Products
                .CountAsync(p => idler.Contains(p.Id) && !p.ArsivlendiMi);

            if (mevcut != idler.Count)
            {
                return "Seçilen ürünlerden biri bulunamadı ya da arşivli.";
            }

            return null;
        }
    }
}
