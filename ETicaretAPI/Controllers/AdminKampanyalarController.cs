using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;
using ETicaretAPI.Services;

namespace ETicaretAPI.Controllers
{
    // ⭐ YENİ (B2) — KAMPANYA / BANNER YÖNETİMİ (admin)
    //
    // Panelin "Bannerlar" sayfası buradan besleniyor: afiş ekleme,
    // görsel yükleme, yayından kaldırma, sıralama, silme.
    [Route("api/admin/kampanyalar")]
    [ApiController]
    [Authorize(Roles = "admin")]
    public class AdminKampanyalarController : ControllerBase
    {
        // Görsellerin gittiği klasör ve kabul edilen yol öneki.
        //
        // ⚠️ Önek KONTROL İÇİN de kullanılıyor: GorselUrl istekten
        // geliyor ve doğrulanmasaydı yönetici (ya da çalınmış bir
        // yönetici oturumu) oraya "/appsettings.json" yazıp silme
        // ucuyla sunucudan dosya sildirebilirdi. "Ön yüze güvenme"
        // kuralı yetkili istekler için de geçerli.
        private const string Klasor = "kampanyalar";
        private const string YolOneki = "/uploads/kampanyalar/";

        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        // ⭐ YENİ — denetim kaydı. Afiş müşterinin ana sayfada gördüğü
        // vitrin: "bu banner nereye gitti" sorusunun cevabı olmalı.
        private readonly DenetimKaydi _denetim;

        public AdminKampanyalarController(
            AppDbContext context,
            IWebHostEnvironment env,
            DenetimKaydi denetim)
        {
            _context = context;
            _env = env;
            _denetim = denetim;
        }


        // Token'dan admin kimliği. Uç [Authorize] altında; 0 savunma amaçlı.
        private int AdminId()
        {
            var talep = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);

            return talep != null && int.TryParse(talep.Value, out var id) ? id : 0;
        }


        // ⚠️ Beyaz liste — varlık serialize edilmiyor.
        // Aciklama (2000 karakter) bilerek dışarıda: eski ve yeni hâlini
        // her kayda koymak denetimi metin arşivine çevirirdi.
        private static Dictionary<string, object?> KampanyaDenetimAlanlari(Kampanya k)
        {
            return new Dictionary<string, object?>
            {
                ["baslik"] = k.Baslik,
                ["kisaAciklama"] = k.KisaAciklama,
                ["bitisMetni"] = k.BitisMetni,
                ["gorselUrl"] = k.GorselUrl,
                ["kuponKodlari"] = k.KuponKodlari,
                ["sira"] = k.Sira,
                ["yayinda"] = k.AktifMi
            };
        }

        // 🔴 GET /api/admin/kampanyalar
        //
        // ⚠️ Yayında olmayanlar DA dönüyor — müşteri ucunun tersine.
        // Yönetici kapattığı afişi göremezse geri açamazdı.
        [HttpGet]
        public async Task<IActionResult> Liste()
        {
            var kayitlar = await _context.Kampanyalar
                .OrderBy(k => k.Sira)
                .ThenBy(k => k.Id)
                .ToListAsync();

            return Ok(kayitlar.Select(k => new
            {
                k.Id,
                k.Baslik,
                k.KisaAciklama,
                k.BitisMetni,
                k.Aciklama,
                k.GorselUrl,
                k.Sira,
                k.AktifMi,
                k.CreatedAt,
                KuponKodlari = KampanyaSatirlari.Bol(k.KuponKodlari),
                Kosullar = KampanyaSatirlari.Bol(k.Kosullar),
            }));
        }

        // 🔴 POST /api/admin/kampanyalar/gorsel   (multipart, alan adı: dosya)
        //
        // ⚠️ GÖRSEL KAMPANYADAN ÖNCE YÜKLENİYOR ve kayda bağlı değil.
        //
        // Alternatif, önce kampanyayı kaydedip sonra görsel eklemekti;
        // o zaman "görseli olmayan kampanya" diye bir ara durum
        // doğardı ve şeritte boş kutu çizilirdi. Bedeli: form
        // doldurulup vazgeçilirse diskte sahipsiz bir dosya kalıyor.
        // Panel bu yüzden yüklemeyi KAYDET anına erteliyor — önizleme
        // tarayıcıda yerel olarak yapılıyor, sunucuya sadece
        // kaydedilecek dosya gidiyor.
        [HttpPost("gorsel")]
        public async Task<IActionResult> GorselYukle([FromForm] IFormFile dosya)
        {
            var hata = await ResimDosyasi.DogrulaAsync(dosya);

            if (hata != null)
            {
                return BadRequest(new { mesaj = hata });
            }

            var url = await ResimDosyasi.DiskeYazAsync(_env, dosya, Klasor);

            return Ok(new { url });
        }

        // 🔴 POST /api/admin/kampanyalar
        [HttpPost]
        public async Task<IActionResult> Ekle([FromBody] KampanyaKaydetDto dto)
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

            var kampanya = new Kampanya
            {
                GorselUrl = dto.GorselUrl,
                CreatedAt = DateTime.UtcNow,
            };

            Doldur(kampanya, dto);

            _context.Kampanyalar.Add(kampanya);
            await _context.SaveChangesAsync();

            // ⭐ YENİ — DENETİM KAYDI.
            // ⚠️ SaveChanges'ten SONRA: etikete giren Id ancak o zaman dolu.
            await _denetim.EkleAsync(
                yapanId: AdminId(),
                hedefId: AdminId(),
                hedefAd: DenetimEtiketi.Kampanya(kampanya.Id, kampanya.Baslik),
                islem: DenetimIslemi.KampanyaEklendi,
                eski: null,
                yeni: DenetimDegeri.Yaz(KampanyaDenetimAlanlari(kampanya)));

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Kampanya oluşturuldu.", id = kampanya.Id });
        }

        // 🔴 PUT /api/admin/kampanyalar/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Guncelle(int id, [FromBody] KampanyaKaydetDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var kampanya = await _context.Kampanyalar.FirstOrDefaultAsync(k => k.Id == id);

            if (kampanya == null)
            {
                return NotFound(new { mesaj = "Kampanya bulunamadı." });
            }

            var hata = await DogrulaAsync(dto);

            if (hata != null)
            {
                return BadRequest(new { mesaj = hata });
            }

            // ⚠️ Görsel değiştiyse ESKİ DOSYA SİLİNİYOR — ama ancak
            // veritabanı yazması başarılı olduktan sonra. Önce silip
            // sonra SaveChanges patlasaydı kayıt eski adresi
            // gösterirken dosya diskte olmazdı: ekranda kırık resim.
            var eskiGorsel = kampanya.GorselUrl;

            // ⚠️ Denetim için eski değerler, atamalardan ÖNCE.
            var oncekiDegerler = KampanyaDenetimAlanlari(kampanya);

            kampanya.GorselUrl = dto.GorselUrl;
            Doldur(kampanya, dto);

            var (degisenEski, degisenYeni) = DenetimDegeri.Degisenler(
                oncekiDegerler, KampanyaDenetimAlanlari(kampanya));

            if (degisenEski.Count > 0)
            {
                await _denetim.EkleAsync(
                    yapanId: AdminId(),
                    hedefId: AdminId(),
                    hedefAd: DenetimEtiketi.Kampanya(kampanya.Id, kampanya.Baslik),
                    islem: DenetimIslemi.KampanyaGuncellendi,
                    eski: DenetimDegeri.Yaz(degisenEski),
                    yeni: DenetimDegeri.Yaz(degisenYeni));
            }

            await _context.SaveChangesAsync();

            if (eskiGorsel != kampanya.GorselUrl)
            {
                ResimDosyasi.DiskDosyasiniSil(_env, eskiGorsel);
            }

            return Ok(new { mesaj = "Kampanya güncellendi." });
        }

        // 🔴 PUT /api/admin/kampanyalar/5/durum
        //
        // Yayına al / yayından kaldır. Ayrı uç olmasının sebebi:
        // listeden tek tıkla yapılıyor ve formun tamamını göndermek
        // gerekmiyor. (Ürünlerdeki "satışa aç/kapat" deseni.)
        [HttpPut("{id}/durum")]
        public async Task<IActionResult> DurumDegistir(int id, [FromBody] StatusToggleDto dto)
        {
            var kampanya = await _context.Kampanyalar.FirstOrDefaultAsync(k => k.Id == id);

            if (kampanya == null)
            {
                return NotFound(new { mesaj = "Kampanya bulunamadı." });
            }

            var oncekiDurum = kampanya.AktifMi;
            kampanya.AktifMi = dto.IsActive;

            // ⚠️ Değişiklik yoksa kayıt da yok.
            if (oncekiDurum != kampanya.AktifMi)
            {
                await _denetim.EkleAsync(
                    yapanId: AdminId(),
                    hedefId: AdminId(),
                    hedefAd: DenetimEtiketi.Kampanya(kampanya.Id, kampanya.Baslik),
                    islem: DenetimIslemi.KampanyaGuncellendi,
                    eski: DenetimDegeri.Yaz("yayinda", oncekiDurum),
                    yeni: DenetimDegeri.Yaz("yayinda", kampanya.AktifMi));
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                mesaj = kampanya.AktifMi ? "Kampanya yayına alındı." : "Kampanya yayından kaldırıldı."
            });
        }

        // 🔴 PUT /api/admin/kampanyalar/sirala
        //
        // Gövde: [3, 1, 7] — istenen sıradaki id listesi.
        //
        // ⚠️ TEK TEK "yukarı taşı" UCU YAZILMADI. O uçta iki kampanyanın
        // sırasını takas etmek gerekir ve iki yönetici aynı anda
        // taşırsa sıra numaraları çakışır. Listenin tamamını yeniden
        // numaralamak son gönderen kazandığı için hep tutarlı kalıyor.
        [HttpPut("sirala")]
        public async Task<IActionResult> Sirala([FromBody] List<int> idler)
        {
            if (idler == null || idler.Count == 0)
            {
                return BadRequest(new { mesaj = "Sıralanacak kampanya yok." });
            }

            var kayitlar = await _context.Kampanyalar
                .Where(k => idler.Contains(k.Id))
                .ToListAsync();

            if (kayitlar.Count != idler.Distinct().Count())
            {
                return BadRequest(new { mesaj = "Listede olmayan bir kampanya gönderildi." });
            }

            for (var i = 0; i < idler.Count; i++)
            {
                var kayit = kayitlar.First(k => k.Id == idler[i]);
                kayit.Sira = i;
            }

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Sıralama kaydedildi." });
        }

        // 🔴 DELETE /api/admin/kampanyalar/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Sil(int id)
        {
            var kampanya = await _context.Kampanyalar.FirstOrDefaultAsync(k => k.Id == id);

            if (kampanya == null)
            {
                return NotFound(new { mesaj = "Kampanya bulunamadı." });
            }

            var gorsel = kampanya.GorselUrl;

            // ⚠️ Kampanya bir DUYURU, ticari kayıt değil: gerçekten
            // siliniyor, arşivlenmiyor. Sipariş ya da kupon kaydına
            // bağlı değil — kuponlar kendi tablosunda duruyor ve
            // kampanya silinince onlara bir şey olmuyor.
            _context.Kampanyalar.Remove(kampanya);

            // ⭐ YENİ — DENETİM KAYDI, satır silinmeden önce.
            await _denetim.EkleAsync(
                yapanId: AdminId(),
                hedefId: AdminId(),
                hedefAd: DenetimEtiketi.Kampanya(kampanya.Id, kampanya.Baslik),
                islem: DenetimIslemi.KampanyaSilindi,
                eski: DenetimDegeri.Yaz(KampanyaDenetimAlanlari(kampanya)),
                yeni: null);

            await _context.SaveChangesAsync();

            // Dosya, kayıt gittikten SONRA siliniyor: sıra tersi olsaydı
            // SaveChanges patladığında kayıt kalır, görseli kalmazdı.
            ResimDosyasi.DiskDosyasiniSil(_env, gorsel);

            return Ok(new { mesaj = "Kampanya silindi." });
        }

        // ---------- YARDIMCILAR ----------

        private static void Doldur(Kampanya kampanya, KampanyaKaydetDto dto)
        {
            kampanya.Baslik = dto.Baslik.Trim();
            kampanya.KisaAciklama = dto.KisaAciklama.Trim();
            kampanya.BitisMetni = dto.BitisMetni.Trim();
            kampanya.Aciklama = dto.Aciklama.Trim();
            kampanya.Sira = dto.Sira;
            kampanya.AktifMi = dto.AktifMi;

            kampanya.KuponKodlari = KampanyaSatirlari.Birlestir(
                dto.KuponKodlari.Select(k => k.Trim().ToUpperInvariant()));

            kampanya.Kosullar = KampanyaSatirlari.Birlestir(dto.Kosullar);
        }

        private async Task<string?> DogrulaAsync(KampanyaKaydetDto dto)
        {
            // ⚠️ Görsel yolu bizim yüklediğimiz klasörü göstermeli.
            // Gerekçe sınıfın başındaki YolOneki notunda.
            if (!dto.GorselUrl.StartsWith(YolOneki))
            {
                return "Görsel adresi geçersiz. Görseli panelden yükle.";
            }

            var kodlar = dto.KuponKodlari
                .Select(k => k.Trim().ToUpperInvariant())
                .Where(k => k.Length > 0)
                .Distinct()
                .ToList();

            if (kodlar.Count > 5)
            {
                return "Bir kampanyada en fazla beş kupon kodu olabilir.";
            }

            // ⚠️⚠️ KUPON KODLARI GERÇEKTEN VAR MI?
            //
            // Mobilde bu kodların yanında sunucudan çekilmiş indirim
            // tutarı gösteriliyor ve müşteri kodu kopyalayıp sepette
            // kullanıyor. Olmayan bir kod yazılabilseydi müşteriye
            // tutulmayacak bir indirim sözü verilmiş olurdu — kampanya
            // metni uydurma olabilir ama İNDİRİM UYDURULMAZ.
            //
            // ⚠️ Kuponun AKTİF olması aranmıyor, yalnızca var olması.
            // Kampanya gelecek hafta başlayacak bir kupon için
            // önceden hazırlanabiliyor; müşteri ekranı zaten kuponu
            // çekemezse o kartı hiç çizmiyor.
            if (kodlar.Count > 0)
            {
                var bulunan = await _context.Coupons
                    .Where(c => kodlar.Contains(c.Code))
                    .Select(c => c.Code)
                    .ToListAsync();

                var eksikler = kodlar.Except(bulunan).ToList();

                if (eksikler.Count > 0)
                {
                    return "Şu kupon kodları sistemde yok: " + string.Join(", ", eksikler);
                }
            }

            if (dto.Kosullar.Count > 10)
            {
                return "En fazla on koşul maddesi yazılabilir.";
            }

            return null;
        }
    }
}
