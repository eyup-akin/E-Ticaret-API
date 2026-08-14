using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Services;

namespace ETicaretAPI.Controllers
{
    // ⭐ YENİ (B2) — KAMPANYALAR, MÜŞTERİ TARAFI
    //
    // Ana sayfadaki afiş şeridi ve kampanya detay ekranı buradan
    // besleniyor.
    //
    // ⚠️ [Authorize] YOK — bilerek. Afişler ana sayfanın en üstünde ve
    // ana sayfa giriş yapmadan da açılıyor. Kilitlesek şerit yalnızca
    // üye olanlara görünürdü; oysa kampanyanın işi tam da üye olmayanı
    // içeri çekmek. (ProductsController'daki kararın aynısı.)
    //
    // ⚠️ YALNIZCA YAYINDAKİLER DÖNÜYOR. Filtre burada, istekte değil:
    // "?aktif=false" gibi bir parametre bırakmak, henüz yayınlanmamış
    // kampanyayı adres çubuğuyla okunabilir yapardı.
    [Route("api/kampanyalar")]
    [ApiController]
    public class KampanyalarController : ControllerBase
    {
        private readonly AppDbContext _context;

        public KampanyalarController(AppDbContext context)
        {
            _context = context;
        }

        // 🟢 GET /api/kampanyalar
        [HttpGet]
        public async Task<IActionResult> Liste()
        {
            // ⚠️ Sıralamada Id ikinci ölçüt: iki kampanya aynı sıra
            // numarasını aldığında SQL Server'ın döndürdüğü sıra
            // garantili değil ve şerit her açılışta yer değiştirirdi.
            var kayitlar = await _context.Kampanyalar
                .Where(k => k.AktifMi)
                .OrderBy(k => k.Sira)
                .ThenBy(k => k.Id)
                .ToListAsync();

            return Ok(kayitlar.Select(Cevir));
        }

        // 🟢 GET /api/kampanyalar/5
        [HttpGet("{id}")]
        public async Task<IActionResult> Detay(int id)
        {
            // ⚠️ Yayında olma koşulu SORGUNUN İÇİNDE, ayrı bir if
            // değil: ayrı yazılan kontrol unutulabilir ve yayından
            // kaldırılmış kampanya doğrudan adresle açılırdı.
            var kampanya = await _context.Kampanyalar
                .FirstOrDefaultAsync(k => k.Id == id && k.AktifMi);

            if (kampanya == null)
            {
                return NotFound(new { mesaj = "Kampanya bulunamadı." });
            }

            return Ok(Cevir(kampanya));
        }

        // Tek dönüşüm noktası: liste ve detay AYNI şekli döndürüyor.
        // Ayrı yazsaydık detaya eklenen bir alan listede eksik kalır ve
        // mobil taraf iki farklı nesneyle uğraşırdı.
        private static object Cevir(Models.Kampanya k) => new
        {
            k.Id,
            k.Baslik,
            k.KisaAciklama,
            k.BitisMetni,
            k.Aciklama,
            k.GorselUrl,
            KuponKodlari = KampanyaSatirlari.Bol(k.KuponKodlari),
            Kosullar = KampanyaSatirlari.Bol(k.Kosullar),
        };
    }
}
