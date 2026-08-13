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


        // 🔴 GET /api/imports/sablon  → örnek Excel şablonunu indirir
        //
        // Şablon iki sayfadan oluşur:
        //   1) Ürünler   → doldurulacak başlıklar + örnek dolu satır
        //   2) Açıklama  → her sütun ne demek, zorunlu mu, format nasıl
        //
        // Başlıklar UrunKolonlari'ndan geldiği için içe aktarmayla
        // GARANTİLİ uyumlu — elle yazılmıyor.
        [HttpGet("sablon")]
        public IActionResult SablonIndir()
        {
            using var wb = new ClosedXML.Excel.XLWorkbook();

            // ----- SAYFA 1: ÜRÜNLER -----
            var ws = wb.Worksheets.Add("Ürünler");

            // Başlık satırı — UrunKolonlari sırasına göre
            for (int i = 0; i < UrunKolonlari.Hepsi.Length; i++)
            {
                var kolon = UrunKolonlari.Hepsi[i];
                var hucre = ws.Cell(1, i + 1);   // satır 1, sütun i+1 (Excel 1'den başlar)

                hucre.Value = kolon.BaslikAdi;

                // Zorunlu sütunları görsel olarak ayır: koyu + renkli arka plan
                hucre.Style.Font.Bold = true;
                hucre.Style.Fill.BackgroundColor = kolon.Zorunlu
                    ? ClosedXML.Excel.XLColor.LightBlue
                    : ClosedXML.Excel.XLColor.LightGray;
            }

            // Barkod sütununun TAMAMINI metin formatına al (başlık hariç aşağısı).
            // Kullanıcı yeni satır eklediğinde de barkodu Excel bozmasın.
            var barkodIndex = System.Array.FindIndex(
                UrunKolonlari.Hepsi, k => k.BaslikAdi == "Barkod");

            if (barkodIndex >= 0)
            {
                ws.Column(barkodIndex + 1).Style.NumberFormat.Format = "@";
            }

            // Örnek dolu satır (2. satır) — kullanıcı formatı görsün
            for (int i = 0; i < UrunKolonlari.Hepsi.Length; i++)
            {
                var kolon = UrunKolonlari.Hepsi[i];
                var hucre = ws.Cell(2, i + 1);

                // Barkod'u METİN olarak yaz. Aksi halde Excel uzun sayıyı
                // bilimsel gösterime (8.69E+12) çevirip YUVARLIYOR — tüm
                // barkodlar aynı değere düşüyor ve tekrar sayılıp eleniyor.
                if (kolon.BaslikAdi == "Barkod")
                {
                    hucre.Style.NumberFormat.Format = "@"; // "@" = metin formatı
                    hucre.SetValue(kolon.OrnekDeger);
                }
                else
                {
                    hucre.Value = kolon.OrnekDeger;
                }
            }

            ws.Columns().AdjustToContents(); // sütunları içeriğe göre genişlet

            // ----- SAYFA 2: AÇIKLAMA -----
            var ac = wb.Worksheets.Add("Açıklama");

            ac.Cell(1, 1).Value = "Sütun";
            ac.Cell(1, 2).Value = "Zorunlu mu?";
            ac.Cell(1, 3).Value = "Açıklama";

            ac.Row(1).Style.Font.Bold = true;

            for (int i = 0; i < UrunKolonlari.Hepsi.Length; i++)
            {
                var kolon = UrunKolonlari.Hepsi[i];
                var satir = i + 2; // başlık 1. satırda, veriler 2'den başlar

                ac.Cell(satir, 1).Value = kolon.BaslikAdi;
                ac.Cell(satir, 2).Value = kolon.Zorunlu ? "Evet" : "Hayır";
                ac.Cell(satir, 3).Value = kolon.Aciklama;
            }

            ac.Columns().AdjustToContents();

            // ----- BELLEKTE .xlsx'e ÇEVİR VE DÖN -----
            // Diske YAZMIYORUZ — dosyayı bellekte üretip doğrudan gönderiyoruz.
            // Şablon her seferinde aynı; saklamanın anlamı yok.
            using var akis = new MemoryStream();
            wb.SaveAs(akis);
            var icerik = akis.ToArray();

            return File(
                icerik,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "urun-sablonu.xlsx");
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
