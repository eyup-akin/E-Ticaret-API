using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Services;

namespace ETicaretAPI.Controllers
{
    // ⭐ YENİ — SİSTEM KAYITLARI (log ekranı)
    //
    // Panelin "Sistem Kayıtları" sayfasındaki üç sekmeyi besliyor:
    // e-posta, girişler, hatalar. Dördüncü sekme (Denetim) mevcut
    // `GET /api/admin/audit-logs` ucundan besleniyor — ikinci bir kopya
    // yazmak filtre/sayfalama mantığını çiftlemek olurdu.
    //
    // ⚠️⚠️ HEPSİ SÜPERADMİNE AÇIK, "admin"e DEĞİL.
    // Bu tablolar adminlerin ne yaptığını gösteriyor; denetlenen kişinin
    // denetim mekanizmasına erişmesi, kendi izini kimin takip ettiğini
    // öğrenmesi demektir. (audit-logs ucundaki kararın aynısı.)
    //
    // ⚠️⚠️ SİLME/DÜZENLEME UCU BİLEREK YAZILMADI. Denetim mekanizması
    // denetlenene kapalı olmalı. Temizlik yalnızca yaş bazlı otomatik
    // iş (LogTemizlikServisi).
    [Route("api/admin/loglar")]
    [ApiController]
    [Authorize(Roles = "superadmin")]
    public class AdminLoglarController : ControllerBase
    {
        // ⚠️ Sayfalama, varsayılan aralık ve sayım kuralları
        // Support/LogSorgusu'nda — denetim sekmesi (AdminController)
        // ikinci tüketici ve iki kopya ayrışamamalı.

        private readonly AppDbContext _context;
        private readonly RaporTarihi _tarih;
        private readonly IEmailGonderici _email;
        private readonly ILogger<AdminLoglarController> _log;

        public AdminLoglarController(
            AppDbContext context,
            RaporTarihi tarih,
            IEmailGonderici email,
            ILogger<AdminLoglarController> log)
        {
            _context = context;
            _tarih = tarih;
            _email = email;
            _log = log;
        }


        // ============================================================
        //  🟣 GET /api/admin/loglar/eposta
        //     ?arama=&olay=&sonuc=&baslangic=&bitis=&page=1&pageSize=20
        //
        //  ⚠️ Bu tablo "GÖNDERDİK Mİ" sorusunu cevaplıyor, "ULAŞTI MI"
        //  sorusunu değil. Teslimat durumu Brevo panelinde.
        // ============================================================
        [HttpGet("eposta")]
        public async Task<IActionResult> Eposta(
            [FromQuery] string? arama,
            [FromQuery] string? olay,
            [FromQuery] string? sonuc,
            [FromQuery] DateTime? baslangic,
            [FromQuery] DateTime? bitis,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var (sayfa, boyut) = SayfaDuzelt(page, pageSize);
            var aralik = Aralik(baslangic, bitis);

            var sorgu = _context.EmailKayitlari
                .Where(x => x.CreatedAt >= aralik.BaslangicUtc
                         && x.CreatedAt < aralik.BitisUtcHaric);

            if (!string.IsNullOrWhiteSpace(olay))
            {
                sorgu = sorgu.Where(x => x.Olay == olay);
            }

            // "basarili" / "hata" — üçüncü bir değer filtreyi kapatıyor.
            if (sonuc == "basarili")
            {
                sorgu = sorgu.Where(x => x.Basarili);
            }
            else if (sonuc == "hata")
            {
                sorgu = sorgu.Where(x => !x.Basarili);
            }

            if (!string.IsNullOrWhiteSpace(arama))
            {
                var a = arama.Trim();
                sorgu = sorgu.Where(x => x.Alici.Contains(a) || x.Konu.Contains(a));
            }

            var (toplam, asildi) = await SayAsync(sorgu);

            var kayitlar = await sorgu
                .OrderByDescending(x => x.CreatedAt)
                .Skip((sayfa - 1) * boyut)
                .Take(boyut)
                .Select(x => new
                {
                    x.Id,
                    alici = x.Alici,
                    konu = x.Konu,
                    olay = x.Olay,
                    basarili = x.Basarili,
                    hataMesaji = x.HataMesaji,
                    mesajId = x.SaglayiciMesajId,

                    // ⚠️ Gövdenin KENDİSİ gönderilmiyor, yalnızca var mı
                    // yok mu. Mail gövdesi kişisel veri içeriyor ve ekranda
                    // gösterilmiyor; tek işi "tekrar gönder" butonunun
                    // çıkıp çıkmayacağını belirlemek.
                    tekrarGonderilebilir = x.GovdeHtml != null,

                    tarih = x.CreatedAt
                })
                .ToListAsync();

            // ⚠️ Olay listesi FİLTREDEN BAĞIMSIZ okunuyor: kullanıcı bir
            // olayı seçmişken menüde sadece o kalsaydı başka bir olaya
            // geçemezdi — kendi kendini kilitleyen bir filtre olurdu.
            var olaylar = await _context.EmailKayitlari
                .Select(x => x.Olay)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            return Ok(Cevap(kayitlar, toplam, asildi, sayfa, boyut, aralik, new { olaylar }));
        }


        // ============================================================
        //  🟣 POST /api/admin/loglar/eposta/5/tekrar-gonder
        //
        //  ⚠️ "Gitmedi" bilgisi tek başına işe yaramaz; yanında bir
        //  düzeltme yolu olmalı. Gövde yalnızca BAŞARISIZ kayıtlarda
        //  saklanıyor, o yüzden yalnızca onlar tekrar gönderilebiliyor.
        // ============================================================
        [HttpPost("eposta/{id}/tekrar-gonder")]
        public async Task<IActionResult> TekrarGonder(int id)
        {
            var kayit = await _context.EmailKayitlari.FindAsync(id);

            if (kayit == null)
            {
                return NotFound(new { mesaj = "Kayıt bulunamadı." });
            }

            if (string.IsNullOrEmpty(kayit.GovdeHtml))
            {
                // ⚠️ 400 değil 409: istek geçerli, engel kaydın MEVCUT
                // DURUMU. Başarılı gönderimlerde gövde bilerek saklanmıyor
                // (sipariş içeriğini ikinci kez arşivlememek için).
                return Conflict(new
                {
                    mesaj = "Bu kaydın gövdesi saklanmıyor, tekrar gönderilemez. "
                          + "Gövde yalnızca gönderilemeyen maillerde tutuluyor."
                });
            }

            if (string.IsNullOrWhiteSpace(kayit.Alici))
            {
                return Conflict(new { mesaj = "Alıcı adresi boş, gönderilecek yer yok." });
            }

            try
            {
                // ⚠️ Gönderici SARMALANMIŞ (KayitTutanEmailGonderici):
                // bu deneme de kendi başına YENİ bir EmailKaydi üretiyor.
                // Yani "kaç kez denendi" tabloda görünüyor.
                await _email.GonderAsync(
                    kayit.Alici, kayit.Konu, kayit.GovdeHtml, kayit.Olay);
            }
            catch (Exception hata)
            {
                _log.LogError(hata, "Tekrar gönderim başarısız. Kayıt: {Id}", id);

                return StatusCode(502, new
                {
                    mesaj = "Tekrar gönderim de başarısız oldu: " + hata.Message
                });
            }

            // ⚠️ GÖVDE SİLİNİYOR — gönderim başarılı olduğu için artık
            // saklanmasının bir sebebi yok. Bırakmak, kişisel veriyi
            // gereksiz yere uzun süre tutmak olurdu (madde 2.6).
            //
            // ⚠️ Kaydın kendisi silinmiyor: "bu mail bir kez gitmemişti"
            // bilgisi denetim değeri taşıyor.
            kayit.GovdeHtml = null;
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Mail tekrar gönderildi." });
        }


        // ============================================================
        //  🟣 GET /api/admin/loglar/girisler
        //
        //  ⚠️ E-posta yazılıyor ama hesabın var olup olmadığı DIŞARIYA
        //  sızmıyor: bu uç yalnızca süperadmine açık ve giriş ucunun
        //  cevabı bundan etkilenmiyor.
        // ============================================================
        [HttpGet("girisler")]
        public async Task<IActionResult> Girisler(
            [FromQuery] string? arama,
            [FromQuery] string? sonuc,
            [FromQuery] bool sadeceBasarisiz = false,
            [FromQuery] DateTime? baslangic = null,
            [FromQuery] DateTime? bitis = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var (sayfa, boyut) = SayfaDuzelt(page, pageSize);
            var aralik = Aralik(baslangic, bitis);

            var sorgu = _context.GirisKayitlari
                .Where(x => x.CreatedAt >= aralik.BaslangicUtc
                         && x.CreatedAt < aralik.BitisUtcHaric);

            if (sadeceBasarisiz)
            {
                sorgu = sorgu.Where(x => x.Sonuc != Models.GirisSonucu.Basarili);
            }

            if (!string.IsNullOrWhiteSpace(sonuc))
            {
                sorgu = sorgu.Where(x => x.Sonuc == sonuc);
            }

            if (!string.IsNullOrWhiteSpace(arama))
            {
                var a = arama.Trim();

                // ⚠️ IP'de de aranıyor: "bu adresten kaç deneme geldi"
                // sorusu bu tablonun en çok sorulan sorusu.
                sorgu = sorgu.Where(x =>
                    x.Email.Contains(a) ||
                    (x.IpAdresi != null && x.IpAdresi.Contains(a)));
            }

            var (toplam, asildi) = await SayAsync(sorgu);

            var kayitlar = await sorgu
                .OrderByDescending(x => x.CreatedAt)
                .Skip((sayfa - 1) * boyut)
                .Take(boyut)
                .Select(x => new
                {
                    x.Id,
                    email = x.Email,
                    sonuc = x.Sonuc,
                    ip = x.IpAdresi,
                    tarih = x.CreatedAt
                })
                .ToListAsync();

            return Ok(Cevap(kayitlar, toplam, asildi, sayfa, boyut, aralik, null));
        }


        // ============================================================
        //  🟣 GET /api/admin/loglar/hatalar
        // ============================================================
        [HttpGet("hatalar")]
        public async Task<IActionResult> Hatalar(
            [FromQuery] string? arama,
            [FromQuery] DateTime? baslangic,
            [FromQuery] DateTime? bitis,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var (sayfa, boyut) = SayfaDuzelt(page, pageSize);
            var aralik = Aralik(baslangic, bitis);

            var sorgu = _context.HataKayitlari
                .Where(x => x.CreatedAt >= aralik.BaslangicUtc
                         && x.CreatedAt < aralik.BitisUtcHaric);

            if (!string.IsNullOrWhiteSpace(arama))
            {
                var a = arama.Trim();
                sorgu = sorgu.Where(x => x.Yol.Contains(a) || x.Mesaj.Contains(a));
            }

            var (toplam, asildi) = await SayAsync(sorgu);

            var kayitlar = await sorgu
                .OrderByDescending(x => x.CreatedAt)
                .Skip((sayfa - 1) * boyut)
                .Take(boyut)
                .Select(x => new
                {
                    x.Id,
                    yol = x.Yol,
                    yontem = x.Yontem,
                    mesaj = x.Mesaj,

                    // ⚠️ Yığın izi LİSTEDE GÖNDERİLMİYOR, yalnızca detay
                    // ucunda. Sayfa başına 20 tam yığın izi taşımak,
                    // ekranın kendisini yavaşlatan bir cevap üretirdi.
                    yiginIziVar = x.YiginIzi != null,

                    kullaniciId = x.KullaniciId,
                    ip = x.IpAdresi,
                    tarih = x.CreatedAt
                })
                .ToListAsync();

            return Ok(Cevap(kayitlar, toplam, asildi, sayfa, boyut, aralik, null));
        }


        // 🟣 GET /api/admin/loglar/hatalar/5 — yığın izi dahil tek kayıt
        [HttpGet("hatalar/{id}")]
        public async Task<IActionResult> HataDetay(int id)
        {
            var kayit = await _context.HataKayitlari
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Id,
                    yol = x.Yol,
                    yontem = x.Yontem,
                    mesaj = x.Mesaj,
                    yiginIzi = x.YiginIzi,
                    kullaniciId = x.KullaniciId,
                    ip = x.IpAdresi,
                    tarih = x.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (kayit == null)
            {
                return NotFound(new { mesaj = "Hata kaydı bulunamadı." });
            }

            return Ok(kayit);
        }


        // ==========================================================
        //  ORTAK YARDIMCILAR
        // ==========================================================

        private static (int Sayfa, int Boyut) SayfaDuzelt(int page, int pageSize)
        {
            return Support.LogSorgusu.SayfaDuzelt(page, pageSize);
        }

        private RaporAraligi Aralik(DateTime? baslangic, DateTime? bitis)
        {
            return Support.LogSorgusu.Aralik(_tarih, baslangic, bitis);
        }

        private static Task<(int Toplam, bool Asildi)> SayAsync<T>(IQueryable<T> sorgu)
        {
            return Support.LogSorgusu.SayAsync(sorgu);
        }

        private static object Cevap(
            object kayitlar,
            int toplam,
            bool asildi,
            int sayfa,
            int boyut,
            RaporAraligi aralik,
            object? ek)
        {
            return new
            {
                kayitlar,
                toplam,

                // ⚠️ Ekran bu bayrağa bakıp "1000+" yazıyor. Sayının
                // kendisini 1001 göndermek, ekranda yanlış bir kesinlik
                // yaratırdı.
                toplamAsildi = asildi,

                sayfa,
                sayfaBoyutu = boyut,
                toplamSayfa = (int)Math.Ceiling(toplam / (double)boyut),

                baslangic = aralik.BaslangicYerel.ToString("yyyy-MM-dd"),
                bitis = aralik.BitisYerel.ToString("yyyy-MM-dd"),

                ek
            };
        }
    }
}
