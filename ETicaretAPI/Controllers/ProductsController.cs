using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;
using System.Net;          // ⭐ YENİ — IPAddress, Dns
using System.Net.Sockets;  // ⭐ YENİ — AddressFamily
using System.Security.Claims;   // ⭐ YENİ — token'dan kullanıcı id'si okumak için
using ETicaretAPI.Services;     // ⭐ YENİ — StokDefteri buradan geliyor


namespace ETicaretAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env; // wwwroot'un yerini bilir
        private readonly IHttpClientFactory _httpFactory; // ⭐ URL'den resim indirmek için


        // ⭐ YENİ — Stok defteri servisi.
        //
        // Neden enjekte ediyoruz da "new StokDefteri(_context)" demiyoruz?
        // Çünkü servis Program.cs'te Scoped kayıtlı ve BİZİMLE AYNI
        // DbContext örneğini alıyor. Elle new'leseydik bile aynı context'i
        // vermemiz gerekirdi — DI bunu bizim yerimize, hatasız yapıyor.
        private readonly StokDefteri _defter;

        // ⭐ YENİ — stok eşiği için mağaza ayarları.
        // "Az stok" sınırı panel, rapor ve mobilde AYNI sayı olmalı;
        // burada elle 5 yazsaydık dördüncü bir kopya olurdu.
        private readonly MagazaAyarlari _ayarlar;


        // Resim yükleme kuralları — tek yerde dursun
        private const long MaxDosyaBoyutu = 5 * 1024 * 1024; // 5 MB
        private static readonly string[] IzinliUzantilar = { ".jpg", ".jpeg", ".png", ".webp" };
        private static readonly string[] IzinliTipler = { "image/jpeg", "image/png", "image/webp" };

        public ProductsController(
            AppDbContext context,
            IWebHostEnvironment env,
            IHttpClientFactory httpFactory,
            StokDefteri defter,              // ⭐ YENİ
            MagazaAyarlari ayarlar)          // ⭐ YENİ (5.3)
        {
            _context = context;
            _env = env;
            _httpFactory = httpFactory;
            _defter = defter;                // ⭐ YENİ
            _ayarlar = ayarlar;              // ⭐ YENİ
        }


        // ⭐ YENİ — MÜŞTERİYE GİDEN STOK BİLGİSİNİ HAZIRLA
        //
        // ⚠️ NEDEN AYRI METOT?
        //
        // Aynı dönüşüm İKİ uçta birden lazım: liste (GetProducts) ve
        // detay (GetProduct). İki yere kopyalasaydık, eşik kuralı
        // değiştiğinde birini güncelleyip diğerini unutmak işten
        // değildi — ve sonuç "listede Stokta, detayda Son 2 ürün"
        // gibi kendi içinde çelişen bir ekran olurdu.
        //
        // ⚠️ ADMIN İÇİN HİÇBİR ŞEY GİZLENMİYOR.
        // Panel envanter yönetiyor, gerçek sayıya ihtiyacı var.
        // Müşteri için ham sayı SIFIRLANIYOR — ekranda gizlemek
        // yetmez, JSON'da giden her şey herkese açıktır.
        private void StokBilgisiniDoldur(List<ProductDto> urunler, bool adminMi)
        {
            var esik = _ayarlar.StokAzEsigi;

            foreach (var u in urunler)
            {
                // Ham stok her zaman DTO'ya yazılmış durumda geliyor;
                // türetilmiş alanları ondan hesaplıyoruz.
                var stok = u.Stock ?? 0;

                if (stok <= 0)
                {
                    u.StokDurumu = "yok";
                    u.KalanAdet = null;
                }
                else if (stok < esik)
                {
                    u.StokDurumu = "az";

                    // ⚠️ Kalan adet SADECE "az" durumunda dolduruluyor.
                    // "var" durumunda göndermek, gizlemeye çalıştığımız
                    // ham sayıyı geri sızdırırdı.
                    u.KalanAdet = stok;
                }
                else
                {
                    u.StokDurumu = "var";
                    u.KalanAdet = null;
                }

                if (!adminMi)
                {
                    u.Stock = null;
                }
            }
        }
            
        // ==========================================================
        //  YARDIMCILAR
        // ==========================================================

        // wwwroot klasörünün diskteki tam yolu
        private string WebKok()
        {
            return string.IsNullOrEmpty(_env.WebRootPath)
                ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
                : _env.WebRootPath;
        }

        // Verilen ürün listesine resimlerini doldurur (tek sorguda — N+1 yok)
        private async Task ResimleriDoldur(List<ProductDto> urunler)
        {
            if (urunler.Count == 0)
            {
                return;
            }

            var idler = urunler.Select(u => u.Id).ToList();

            var resimler = await _context.ProductImages
                .Where(r => idler.Contains(r.ProductId))
                .OrderByDescending(r => r.IsMain)   // önce ANA resim
                .ThenBy(r => r.SortOrder)           // sonra yükleme sırası
                .ToListAsync();

            foreach (var urun in urunler)
            {
                var kendiResimleri = resimler
                    .Where(r => r.ProductId == urun.Id)
                    .ToList();

                urun.Images = kendiResimleri
                    .Select(r => new ProductImageDto
                    {
                        Id = r.Id,
                        Url = r.Url,
                        IsMain = r.IsMain,
                        SortOrder = r.SortOrder
                    })
                    .ToList();

                // Ana resim varsa o, yoksa ilk resim, o da yoksa null
                var ana = kendiResimleri.FirstOrDefault(r => r.IsMain)
                          ?? kendiResimleri.FirstOrDefault();

                urun.MainImageUrl = ana?.Url;
            }
        }


        // ⭐ YENİ — İSTEĞİ YAPAN KULLANICININ ID'Sİ
        //
        // Neden token'dan okuyoruz da istekten almıyoruz?
        // "Kimlik istekten değil token'dan okunur." İstemci gövdeye
        // istediği id'yi yazabilir; token imzalı, oynanamaz.
        //
        // Neden int? (nullable) döndürüyor?
        // StockMovement.KullaniciId zaten nullable — sistem işlerinin
        // (Hangfire) bir "yapan"ı yok. Bu uçlar [Authorize] altında
        // olduğu için pratikte hiç null gelmeyecek, ama null dönmek
        // çökmekten iyidir: defter kaydı "yapan bilinmiyor" diye
        // yazılır, istek başarısız olmaz.
        //
        // (OrdersController'daki GetUserId() int döndürüyor çünkü orada
        //  kullanıcı id'si iş mantığının zorunlu parçası — sahiplik
        //  kontrolünde kullanılıyor. Burada sadece bilgi amaçlı.)
        private int? GetUserId()
        {
            var talep = User.FindFirst(ClaimTypes.NameIdentifier);

            if (talep != null && int.TryParse(talep.Value, out var id))
            {
                return id;
            }

            return null;
        }

        // Verilen ürün listesine puan özetini doldurur (tek sorguda — N+1 yok)
        private async Task PuanlariDoldur(List<ProductDto> urunler)
        {
            if (urunler.Count == 0)
            {
                return;
            }

            var idler = urunler.Select(u => u.Id).ToList();

            var puanlar = await _context.Reviews
                // ⭐ YENİ — gizli yorumlar ortalamaya ve sayıya girmez.
                //
                // Bu satır olmasaydı iş yarım kalırdı: yorum listeden
                // kaybolur ama verdiği 1 yıldız ortalamayı aşağı
                // çekmeye devam ederdi. Ekranda "5 yorum (2,4 puan)"
                // yazıp altta 4 yorum listelenirdi — kullanıcı beşinci
                // yorumu arar, bulamaz.
                //
                // Kural: bir kaydı görünürlükten çıkarıyorsan, o kayıttan
                // TÜRETİLEN her şeyi de çıkarmalısın.
                .Where(r => idler.Contains(r.ProductId) && !r.IsHidden)
                .GroupBy(r => r.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    Ortalama = g.Average(x => x.Rating),
                    Sayi = g.Count()
                })
                .ToListAsync();

            foreach (var urun in urunler)
            {
                var p = puanlar.FirstOrDefault(x => x.ProductId == urun.Id);
                if (p != null)
                {
                    urun.AverageRating = Math.Round(p.Ortalama, 1);
                    urun.ReviewCount = p.Sayi;
                }
            }
        }


        // Ürünlere favori sayısını doldurur (tek sorguda)
        private async Task FavorileriDoldur(List<ProductDto> urunler)
        {
            if (urunler.Count == 0)
            {
                return;
            }

            var idler = urunler.Select(u => u.Id).ToList();

            var sayilar = await _context.Favorites
                .Where(f => idler.Contains(f.ProductId))
                .GroupBy(f => f.ProductId)
                .Select(g => new { ProductId = g.Key, Sayi = g.Count() })
                .ToListAsync();

            foreach (var urun in urunler)
            {
                var s = sayilar.FirstOrDefault(x => x.ProductId == urun.Id);
                urun.FavoriteCount = s?.Sayi ?? 0;
            }
        }


        // Diskteki fiziksel dosyayı siler (yoksa sessizce geçer)
        private void DiskDosyasiniSil(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return;
            }

            // "/uploads/urunler/a.jpg" → "uploads\urunler\a.jpg"
            var goreliYol = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var tamYol = Path.Combine(WebKok(), goreliYol);

            if (System.IO.File.Exists(tamYol))
            {
                System.IO.File.Delete(tamYol);
            }
        }

        // GERÇEK KONTROL: dosyanın İÇİNE bak.
        // Uzantı ve ContentType istemciden gelir → yalan olabilir.
        // İlk byte'lar dosyanın kendisindedir → yalan söylenemez.
        private static async Task<bool> GercektenResimMi(IFormFile dosya)
        {
            using var akis = dosya.OpenReadStream();

            var baslik = new byte[12];
            var okunan = await akis.ReadAsync(baslik, 0, 12);

            if (okunan < 12)
            {
                return false; // 12 byte bile yoksa resim değildir
            }

            // JPEG:  FF D8 FF
            if (baslik[0] == 0xFF && baslik[1] == 0xD8 && baslik[2] == 0xFF)
            {
                return true;
            }

            // PNG:  89 50 4E 47 0D 0A 1A 0A
            if (baslik[0] == 0x89 && baslik[1] == 0x50 &&
                baslik[2] == 0x4E && baslik[3] == 0x47)
            {
                return true;
            }

            // WEBP:  "RIFF" ....  "WEBP"
            if (baslik[0] == 0x52 && baslik[1] == 0x49 &&
                baslik[2] == 0x46 && baslik[3] == 0x46 &&
                baslik[8] == 0x57 && baslik[9] == 0x45 &&
                baslik[10] == 0x42 && baslik[11] == 0x50)
            {
                return true;
            }

          
            
            return false;
        }



        // URL'den gelen ham byte'lar için: gerçek resim mi kontrolü + doğru uzantı.
        // Resim değilse null döner. (Uzantıyı URL'den değil, içerikten belirliyoruz.)
        private static string? ResimUzantisiBul(byte[] veri)
        {
            if (veri.Length < 12)
            {
                return null;
            }

            // JPEG: FF D8 FF
            if (veri[0] == 0xFF && veri[1] == 0xD8 && veri[2] == 0xFF)
            {
                return ".jpg";
            }

            // PNG: 89 50 4E 47
            if (veri[0] == 0x89 && veri[1] == 0x50 && veri[2] == 0x4E && veri[3] == 0x47)
            {
                return ".png";
            }

            // WEBP: "RIFF" .... "WEBP"
            if (veri[0] == 0x52 && veri[1] == 0x49 && veri[2] == 0x46 && veri[3] == 0x46 &&
                veri[8] == 0x57 && veri[9] == 0x45 && veri[10] == 0x42 && veri[11] == 0x50)
            {
                return ".webp";
            }

            return null;
        }




        // ==========================================================
        //  ÜRÜN ENDPOINT'LERİ
        // ==========================================================

        // 🟢 GET /api/products?categoryId=2&search=nike&aktif=false
        [HttpGet]
        public async Task<IActionResult> GetProducts(
            [FromQuery] int? categoryId,
            [FromQuery] string? search,
            [FromQuery] bool? aktif)          // ⭐ YENİ — sadece admin için anlamlı
        {
            // Rolü bir kez okuyup değişkene alıyoruz. Aşağıda iki ayrı yerde
            // lazım olacak; her seferinde token'daki claim listesini taramanın
            // anlamı yok.
            var adminMi = User.IsInRole("admin");

            var query = _context.Products.AsQueryable();

            // ⭐ YENİ — GÖRÜNÜRLÜK KİLİDİ
            //
            // Müşteri ve misafir SADECE satıştaki ürünleri görür.
            //
            // Dikkat: "aktif" parametresini bilerek sadece admin dalında
            // okuyoruz. Müşteri ?aktif=false yazarak pasif ürünleri
            // listeletemez — o parametre onun dalında hiç değerlendirilmiyor.
            // İstekten gelen hiçbir değer bu sınırı gevşetemez.
            if (!adminMi)
            {
                query = query.Where(p => p.IsActive);
            }
            else if (aktif.HasValue)
            {
                // Admin panelinde "Sadece pasifler" / "Sadece aktifler"
                // sekmesi için. Parametre gelmezse admin HEPSİNİ görür.
                query = query.Where(p => p.IsActive == aktif.Value);
            }

            if (categoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == categoryId.Value);
            }

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(p => p.Name.Contains(search));
            }

            var products = await query
                .Select(p => new ProductDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Price = p.Price,
                    Stock = p.Stock,
                    CategoryId = p.CategoryId,
                    Barcode = p.Barcode,
                    Cost = p.Cost,
                    IsActive = p.IsActive,      // ⭐ YENİ
                    VatRate = p.VatRate         // ⭐ YENİ
                })
                .ToListAsync();

            // Maliyet hassas bilgi: admin değilse hepsini null'a çek.
            // Böylece müşteriye/misafire maliyet GİTMEZ.
            if (!adminMi)                        // ⭐ değişti: artık değişkeni kullanıyor
            {
                foreach (var u in products)
                {
                    u.Cost = null;
                }
            }

            // ⭐ YENİ — türetilmiş stok bilgisi + müşteride ham stoğu sil
            StokBilgisiniDoldur(products, adminMi);

            await ResimleriDoldur(products);
            await PuanlariDoldur(products);

            return Ok(products);
        }


        // 🟢 GET /api/products/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(int id)
        {
            var product = await _context.Products.FindAsync(id);

            if (product == null)
            {
                return NotFound(new { mesaj = "Ürün bulunamadı biladerim!" });
            }

            var adminMi = User.IsInRole("admin");

            // ⭐ YENİ — pasif ürün müşteriye "hiç yokmuş gibi" görünür.
            //
            // Neden yukarıdakiyle AYNI mesaj ve AYNI 404:
            // "Bu ürün var ama satışta değil" demek, listede görünmeyen bir
            // kaydın varlığını sızdırır. Projedeki kural: yetkisiz veya
            // görünmez erişimde 404 > 403. Aktif oturumlarda da bunu
            // uygulamıştık.
            //
            // Kabul ettiğimiz yan etki: eski bir bağlantıya veya paylaşılan
            // linke tıklayan müşteri "bulunamadı" görür. Alternatifi,
            // satın alınamayacak bir ürünü satın alınabilir göstermek —
            // o daha kötü bir deneyim.
            if (!product.IsActive && !adminMi)
            {
                return NotFound(new { mesaj = "Ürün bulunamadı biladerim!" });
            }

            var dto = new ProductDto
            {
                Id = product.Id,
                Name = product.Name,
                Price = product.Price,
                Stock = product.Stock,
                CategoryId = product.CategoryId,
                Barcode = product.Barcode,
                IsActive = product.IsActive,                  // ⭐ YENİ
                // Maliyet sadece admin isteğinde dolsun, değilse null kalsın
                Cost = adminMi ? product.Cost : null
                ,

                // ⭐ YENİ — açıklama SADECE burada dolduruluyor.
                //
                // GetProducts (liste ucu) bu alanı bilerek atlıyor;
                // orada null gidiyor ve mobil de zaten kullanmıyor.
                Description = product.Description,

                // ⭐ YENİ — KDV oranı. Liste ucunda da dolu geliyor
                // (Description'ın aksine) — tek int, veri yükü yok.
                VatRate = product.VatRate
            };

            // ⭐ YENİ — liste ucuyla AYNI dönüşüm.
            // Ayrı yazsaydık "listede Stokta, detayda Son 2 ürün" gibi
            // kendi içinde çelişen bir ekran çıkabilirdi.
            StokBilgisiniDoldur(new List<ProductDto> { dto }, adminMi);

            await ResimleriDoldur(new List<ProductDto> { dto });
            await PuanlariDoldur(new List<ProductDto> { dto });
            await FavorileriDoldur(new List<ProductDto> { dto });

            // ⭐ YENİ (5.5) — bu müşterinin bekleyen stok bildirimi var mı?
            //
            // ⚠️ Yalnızca DETAY ucunda dolduruluyor, listede değil.
            // Liste ucunda doldursaydık her sayfa için ek bir sorgu
            // (ya da 20 satırlık bir IN) gerekirdi; oysa buton sadece
            // detay sayfasında var. Description'da verilen kararın
            // aynısı: maliyeti faydasından büyük olanı taşıma.
            //
            // ⚠️ NotifiedAt == null şartı önemli: gönderilmiş bir
            // kayıt "bekleyen talep" değildir. Şartı koymasaydık,
            // bir kez bildirim almış müşteri ürün tekrar tükendiğinde
            // butonu "talebin var" halinde görür ve yeniden talep
            // bırakamazdı.
            var kullaniciId = GetUserId();

            if (kullaniciId != null)
            {
                dto.StokBildirimiVar = await _context.StockAlerts
                    .AnyAsync(s => s.UserId == kullaniciId.Value
                                && s.ProductId == id
                                && s.NotifiedAt == null);
            }

            return Ok(dto);
        }


        // 🔴 GET /api/products/5/stok-hareketleri?page=1&pageSize=20
        //
        // Bir ürünün stok DEFTERİ. "Şu an kaç adet var" sorusunun cevabı
        // Product.Stock'ta; "nasıl bu hale geldi" sorusunun cevabı burada.
        //
        // Neden sadece admin?
        // Rakip firma bir ürünün satış hızını buradan hesaplayabilir:
        // son 30 günde kaç adet 'satis' hareketi olduğuna bakması yeterli.
        // Bu ticari bir bilgi. Üç katmanlı yetkinin en dıştaki (ve tek
        // gerçek olan) katmanı burası.
        // ============================================================
        //  ⭐ YENİ (5.5) — STOK BİLDİRİMİ ("stoka gelince haber ver")
        //
        //  İki uç: talep bırak (POST) ve vazgeç (DELETE).
        //  Talebin var olup olmadığı ürün detayında dönüyor.
        // ============================================================

        // 🟡 POST /api/products/5/stok-bildirimi
        [Authorize]
        [HttpPost("{id}/stok-bildirimi")]
        public async Task<IActionResult> StokBildirimiIste(int id)
        {
            var userId = GetUserId();

            if (userId == null)
            {
                return Unauthorized(new { mesaj = "Giriş yapman gerekiyor." });
            }

            // Ürün var mı VE satışta mı?
            //
            // ⚠️ Stok kontrolü YAPMIYORUZ — bilerek.
            // "Sadece tükenmişken talep bırakılabilir" deseydik şu
            // yarış çıkardı: müşteri sayfayı tükenmiş görürken tam o
            // anda stok girer, talep reddedilir ve müşteri neden
            // reddedildiğini anlamaz. Stokta olan ürüne talep bırakmak
            // zararsız: tarama zaten bir sonraki turda maili atıp
            // kaydı kapatır.
            var urunVar = await _context.Products
                .AnyAsync(p => p.Id == id && p.IsActive);

            if (!urunVar)
            {
                return NotFound(new { mesaj = "Ürün bulunamadı." });
            }

            // ---------- VARSA TAZELE, YOKSA EKLE ----------
            //
            // ⚠️ Önce güncellemeyi deniyoruz, sonucuna bakıyoruz —
            // "önce sorgula, yoksa ekle" değil. Sepetteki upsert
            // deseninin aynısı.
            //
            // Tazeleme neden gerekli? Müşteri daha önce bu ürün için
            // bildirim almış olabilir (NotifiedAt dolu). Ürün tekrar
            // tükenip tekrar geldiğinde yeniden haber almak istiyorsa
            // aynı satırı yeniden açıyoruz — ikinci satır açmıyoruz,
            // benzersiz indeks zaten buna izin vermezdi.
            var etkilenen = await _context.StockAlerts
                .Where(s => s.UserId == userId.Value && s.ProductId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NotifiedAt, (DateTime?)null));

            if (etkilenen == 0)
            {
                var yeni = new StockAlert
                {
                    ProductId = id,
                    UserId = userId.Value,
                    CreatedAt = DateTime.UtcNow,
                    NotifiedAt = null
                };

                _context.StockAlerts.Add(yeni);

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Tam bu anda başka bir istek satırı açtı ve
                    // benzersiz indeks bizi reddetti. Hata değil,
                    // beklenen yarış sonucu — istenen durum zaten
                    // oluştu (talep kayıtlı).
                    //
                    // ⚠️ Başarısız Add hâlâ tracker'da "Added";
                    // detach etmezsek sonraki SaveChanges aynı
                    // INSERT'i tekrar dener.
                    _context.Entry(yeni).State = EntityState.Detached;
                }
            }

            return Ok(new { mesaj = "Ürün stoğa geldiğinde sana haber vereceğiz." });
        }

        // 🟡 DELETE /api/products/5/stok-bildirimi
        [Authorize]
        [HttpDelete("{id}/stok-bildirimi")]
        public async Task<IActionResult> StokBildiriminiIptalEt(int id)
        {
            var userId = GetUserId();

            if (userId == null)
            {
                return Unauthorized(new { mesaj = "Giriş yapman gerekiyor." });
            }

            // ⚠️ Sahiplik kontrolü SORGUYA dahil (ayrı bir if değil).
            // Ayrı yazılsaydı unutulabilirdi; bu haliyle başkasının
            // kaydına dokunmak imkânsız.
            //
            // Kayıt yoksa da 200 dönüyoruz: istenen son durum
            // ("bildirim istemiyorum") zaten sağlanmış. 404 dönmek,
            // iki kez basan müşteriye hata göstermek olurdu.
            await _context.StockAlerts
                .Where(s => s.UserId == userId.Value && s.ProductId == id)
                .ExecuteDeleteAsync();

            return Ok(new { mesaj = "Bildirim isteğin kaldırıldı." });
        }


        [Authorize(Roles = "admin")]
        [HttpGet("{id}/stok-hareketleri")]
        public async Task<IActionResult> GetStokHareketleri(
            int id,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            // ---- SAYFALAMA PARAMETRELERİNİ SINIRLA ----
            //
            // İstemciden gelen hiçbir sayıya güvenmiyoruz. pageSize=999999
            // yazan biri tek istekle tüm tabloyu belleğe çektirebilirdi.
            // Üst sınır projedeki diğer uçlarla aynı (100).
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            // ---- ÜRÜN VAR MI? ----
            //
            // Ürünü ayrıca çekiyoruz çünkü iki şeye ihtiyacımız var:
            //   • mevcut stok (kontrol hesabı için)
            //   • ürün adı (ekranın başlığı için)
            //
            // Yoksa 404: olmayan ürünün defteri de olmaz.
            var urun = await _context.Products
                .Where(p => p.Id == id)
                .Select(p => new { p.Id, p.Name, p.Stock })
                .FirstOrDefaultAsync();

            if (urun == null)
            {
                return NotFound(new { mesaj = "Ürün bulunamadı!" });
            }

            // ---- SORGUYU KUR (henüz veritabanına GİTMİYOR) ----
            //
            // IQueryable: bu satır bir SQL çalıştırmıyor, sadece "ne
            // soracağımızı" tarif ediyor. Aşağıda üç farklı şey için
            // (sayım, toplam, liste) yeniden kullanacağız ve her biri
            // TEK SQL'e derlenecek.
            //
            // List<> olsaydı tüm hareketleri belleğe çekip orada
            // uğraşırdık — 50.000 satırlık bir üründe felaket.
            var sorgu = _context.StockMovements
                .Where(h => h.ProductId == id);

            // ---- DEĞİŞMEZ KONTROLÜ ----
            //
            // Bakiye/defter deseninin bedeli şu eşitlik:
            //     Product.Stock == SUM(StockMovement.Miktar)
            //
            // Bu sorguyu elle çalıştırmayı unuturuz. Cevaba gömünce
            // ekranda sürekli durur — bozulduğu gün fark edilir.
            //
            // ⚠️ (int?) CAST'İ ŞART.
            // SQL'de SUM boş küme üzerinde 0 değil NULL döner. Cast
            // olmadan EF bunu int'e map etmeye çalışır ve hiç hareketi
            // olmayan üründe exception fırlar. ?? 0 ile varsayılanı
            // veriyoruz.
            //
            // ⚠️ ?? kullanıyoruz, || değil: sıfır burada GEÇERLİ bir
            // değer (defter toplamı gerçekten 0 olabilir).
            var defterToplami = await sorgu.SumAsync(h => (int?)h.Miktar) ?? 0;

            // ⚠️ Toplam sayıyı SAYFALAMADAN ÖNCE alıyoruz.
            // Sonra alsaydık en fazla pageSize kadar çıkardı ve
            // "toplam 340 hareket" bilgisi hep yanlış olurdu.
            var toplam = await sorgu.CountAsync();

            // ---- HAREKET LİSTESİ ----
            var hareketler = await sorgu
                // En yeni üstte — deftere bakan kişi "en son ne oldu"
                // sorusuyla gelir.
                .OrderByDescending(h => h.CreatedAt)

                // ⚠️ İKİNCİL SIRALAMA ÖLÇÜTÜ — ATLAMA.
                //
                // Tek siparişte 3 ürün varsa 3 hareket AYNI anda yazılır
                // ve CreatedAt değerleri datetime2 hassasiyetinde birebir
                // aynı olabilir. Eşit değerlerde SQL Server garantili sıra
                // vermez: aynı kayıt 1. sayfada da 2. sayfada da çıkabilir,
                // başka bir kayıt hiç görünmeyebilir.
                //
                // Id benzersiz olduğu için sırayı kesinleştiriyor.
                .ThenByDescending(h => h.Id)

                .Skip((page - 1) * pageSize)   // SQL: OFFSET
                .Take(pageSize)                // SQL: FETCH NEXT

                .Select(h => new
                {
                    id = h.Id,
                    tarih = h.CreatedAt,

                    sebep = h.Sebep,
                    miktar = h.Miktar,              // işaretli: +5 / −3
                    oncekiStok = h.OncekiStok,
                    sonrakiStok = h.SonrakiStok,

                    yapanId = h.KullaniciId,

                    // ⚠️ ALT SORGU — JOIN DEĞİL. BU BİLİNÇLİ.
                    //
                    // KullaniciId nullable (sistem işlerinde boş kalır).
                    // LINQ'teki "join ... on ... equals" INNER JOIN'e
                    // derlenir ve eşleşmeyen satırı KOMPLE DÜŞÜRÜR —
                    // yani sistem kayıtları listede hiç görünmez, ama
                    // defter toplamına dahil olur. Sonuç: "liste ile
                    // toplam neden tutmuyor" muamması.
                    //
                    // Alt sorgu eşleşme bulamazsa NULL döner ve satır
                    // YERİNDE KALIR. LEFT JOIN'in yaptığı işi yapıyor
                    // ama LINQ'te çok daha okunaklı.
                    //
                    // Performans endişesi yok: tek SQL üretiliyor (N+1
                    // değil) ve arama primary key üzerinden.
                    yapan = _context.Users
                        .Where(u => u.Id == h.KullaniciId)
                        .Select(u => u.FullName)
                        .FirstOrDefault(),

                    referansTipi = h.ReferansTipi,
                    referansId = h.ReferansId,

                    // Teknik anahtar (Id) ≠ iş anahtarı (OrderNumber).
                    // Ekranda "Sipariş 42" değil "SP-260724-4821" yazsın.
                    //
                    // Koşulu ternary ile dışarıda değil, Where'in İÇİNDE
                    // yazıyoruz — EF'in çeviremeyeceği bir ifade riski
                    // kalmasın. ReferansTipi "Order" değilse hiçbir satır
                    // eşleşmez, null döner.
                    siparisNo = _context.Orders
                        .Where(o => o.Id == h.ReferansId && h.ReferansTipi == "Order")
                        .Select(o => o.OrderNumber)
                        .FirstOrDefault(),

                    aciklama = h.Aciklama
                })
                .ToListAsync();

            var toplamSayfa = (int)Math.Ceiling(toplam / (double)pageSize);

            return Ok(new
            {
                urunId = urun.Id,
                urunAdi = urun.Name,

                // Ekranın üstünde duracak kontrol bloğu.
                // fark != 0 ise "defter eksik başladı VEYA bir yazma
                // noktası atlandı" demektir. Hangisi olduğunu farkın
                // DEĞİŞİP değişmediği söyler.
                kontrol = new
                {
                    mevcutStok = urun.Stock,
                    defterToplami = defterToplami,
                    fark = urun.Stock - defterToplami
                },

                hareketler = hareketler,
                toplam = toplam,
                sayfa = page,
                sayfaBoyutu = pageSize,
                toplamSayfa = toplamSayfa
            });
        }

        // 🔴 POST /api/products
        [Authorize(Roles = "admin")]
        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] ProductCreateDto dto)
        {
            var barkod = dto.Barcode.Trim();

            // Aynı barkod başka bir üründe var mı? (veritabanı da engelliyor ama
            // burada önden kontrol edip kullanıcıya anlaşılır mesaj veriyoruz)
            var barkodVar = await _context.Products.AnyAsync(p => p.Barcode == barkod);
            if (barkodVar)
            {
                return BadRequest(new { mesaj = "Bu barkod zaten başka bir üründe kayıtlı!" });
            }

            var product = new Product
            {
                Name = dto.Name,
                Barcode = barkod,
                Price = dto.Price,
                Cost = dto.Cost,
                Stock = dto.Stock,
                CategoryId = dto.CategoryId,
                IsActive = dto.IsActive      // DTO varsayılanı true

                ,

                // ⭐ YENİ — boşsa null yazıyoruz, boş string değil.
                // Projede "değer yok" durumunun TEK temsili NULL.
                // İleride "açıklaması olmayan ürünler" filtresi
                // yazarsak boş string'ler o listeye düşmesin.
                Description = string.IsNullOrWhiteSpace(dto.Description)
                    ? null
                    : dto.Description.Trim(),

                // ⭐ YENİ — KDV oranı.
                //
                // Doğrulaması DTO'daki [KdvOraniGecerli] özniteliğinde
                // yapılıyor; buraya sadece geçerli oranlar ulaşabiliyor.
                // Burada ikinci bir kontrol yazmak, kuralın iki yerde
                // yaşaması demek olurdu.
                VatRate = dto.VatRate

            };

            // ⭐ YENİ — TRANSACTION
            //
            // Neden burada transaction var da UpdateProduct'ta yok?
            // Çünkü burada İKİ ayrı SaveChanges var ve bunlar zorunlu:
            //   1) Ürünü yaz  → ancak o zaman product.Id oluşur
            //   2) Hareketi yaz → ProductId olarak o Id'yi kullanır
            //
            // Araya bir hata girerse "ürün var ama defter kaydı yok"
            // durumu oluşur ve tam da engellemeye çalıştığımız şey olur:
            // bakiye ile defter arasındaki fark DEĞİŞİR.
            //
            // try/catch yazmıyoruz: 'using' sayesinde Commit'e ulaşılmadan
            // metottan çıkılırsa transaction otomatik geri alınır (rollback).
            using var transaction = await _context.Database.BeginTransactionAsync();

            _context.Products.Add(product);
            await _context.SaveChangesAsync();   // 1. yazma → product.Id artık dolu

            // ⭐ YENİ — DEFTERE BAŞLANGIÇ STOĞU KAYDI
            //
            // oncekiStok = 0 çünkü ürün bu satırdan önce hiç yoktu.
            // miktar da doğrudan dto.Stock: fark = yeni(50) - eski(0) = 50.
            //
            // Sebep neden "manuel"? Çünkü bu hareketi bir admin, elle,
            // panelden yaptı. "excel" toplu içe aktarma için ayrılmış.
            //
            // Referans alanları null: bu hareket bir siparişten veya içe
            // aktarma işinden doğmadı, doğrudan insan kararı.
            //
            // ⚠️ Stok 0 girilirse Ekle() hiç kayıt yazmaz (miktar == 0
            //    kontrolü servisin içinde) — ve bu doğru davranış:
            //    "0'dan 0'a" bir hareket değildir, defteri gürültüyle
            //    doldurmanın anlamı yok.
            _defter.Ekle(
                urunId: product.Id,
                miktar: product.Stock,
                oncekiStok: 0,
                sebep: StokSebep.Manuel,
                kullaniciId: GetUserId(),
                referansTipi: null,
                referansId: null,
                aciklama: "Ürün oluşturuldu");

            await _context.SaveChangesAsync();   // 2. yazma → stok hareketi
            await transaction.CommitAsync();     // ikisi birlikte kesinleşti

            // id'yi döndürüyoruz — panel bunu alıp hemen resim yükleyecek
            return Ok(new { mesaj = "Ürün eklendi biladerim!", id = product.Id });
        }



        // 🔴 PUT /api/products/5
        [Authorize(Roles = "admin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(int id, [FromBody] ProductCreateDto dto)
        {
            var product = await _context.Products.FindAsync(id);

            if (product == null)
            {
                return NotFound(new { mesaj = "Güncellenecek ürün bulunamadı!" });
            }

            var barkod = dto.Barcode.Trim();

            // Aynı barkod BAŞKA bir üründe var mı? (kendisi hariç tutuluyor)
            var barkodVar = await _context.Products
                .AnyAsync(p => p.Barcode == barkod && p.Id != id);
            if (barkodVar)
            {
                return BadRequest(new { mesaj = "Bu barkod zaten başka bir üründe kayıtlı!" });
            }

            // ⭐ YENİ — ESKİ STOĞU, ÜZERİNE YAZILMADAN ÖNCE YAKALA
            //
            // ⚠️ Bu satırın YERİ kritik. Bir alt bloktaki
            //    "product.Stock = dto.Stock" satırı çalıştığı anda eski
            //    değer bellekten kaybolur. Deftere hem "önceki stok" hem
            //    de "fark" yazacağımız için ikisine de ihtiyacımız var.
            //
            //    Sonradan okusaydık miktar hep 0 çıkardı ve Ekle() hiçbir
            //    kayıt yazmazdı — patlamayan, sessiz bir hata. En kötüsü.
            var eskiStok = product.Stock;

            product.Name = dto.Name;
            product.Barcode = barkod;
            product.Price = dto.Price;
            product.Cost = dto.Cost;
            product.Stock = dto.Stock;
            product.CategoryId = dto.CategoryId;
            product.IsActive = dto.IsActive;

            // ⭐ YENİ — açıklama
            product.Description = string.IsNullOrWhiteSpace(dto.Description)
                ? null
                : dto.Description.Trim();

            // ⭐ YENİ — KDV oranı.
            //
            // ⚠️ Bu değişiklik GEÇMİŞ SİPARİŞLERİ ETKİLEMEZ. Oran
            // sipariş anında OrderItem'a kopyalanıyor; buradaki
            // güncelleme yalnızca bundan SONRAKİ siparişler için
            // geçerli. Dondurma deseninin tam olarak var oluş sebebi.
            product.VatRate = dto.VatRate;

            // ⭐ YENİ — DEFTERE FARK KAYDI
            //
            // miktar İŞARETLİ bir sayı:
            //   stok 12 → 50 ise  miktar = +38  (giriş)
            //   stok 50 → 12 ise  miktar = −38  (çıkış)
            // Ayrı bir "yon" kolonu yok; toplam almak tek SUM ile bitsin diye.
            //
            // Admin stoğa hiç dokunmadıysa miktar 0 olur ve Ekle() hiçbir
            // şey yazmaz. Yani her "Kaydet" tıklamasında deftere çöp
            // birikmiyor — sadece gerçek değişiklikler kaydediliyor.
            _defter.Ekle(
                urunId: product.Id,
                miktar: dto.Stock - eskiStok,
                oncekiStok: eskiStok,
                sebep: StokSebep.Manuel,
                kullaniciId: GetUserId(),
                referansTipi: null,
                referansId: null,
                aciklama: "Ürün formundan güncellendi");

            // ⭐ TEK SaveChanges — ve bu yeterli.
            //
            // Ürünün güncellenmesi ile stok hareketinin eklenmesi aynı
            // context'te bekliyor. SaveChanges kendi içinde bir transaction
            // açıp ikisini birlikte yazar: ya ikisi de olur, ya hiçbiri.
            // Bu yüzden burada AYRICA transaction açmak gereksiz kod olurdu.
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Ürün güncellendi biladerim!", id = product.Id });
        }




        // 🔴 PUT /api/products/5/durum — satışa aç / satıştan kaldır
        //
        // Neden ayrı endpoint? PUT /api/products/5 zaten aktifliği yazıyor,
        // ama o metot TÜM ürün formunu bekliyor: ad, barkod, fiyat, maliyet,
        // stok, kategori. Admin panelindeki ÜRÜN LİSTESİNDE bu bilgilerin
        // hepsi elimizde yok (maliyet listede gösterilmiyor mesela).
        // Eksik gönderirsek o alanları sıfırla ezeriz.
        //
        // Kural: tek alanlık işlem = tek alanlık endpoint.
        //
        // StatusToggleDto'yu yeniden kullanıyoruz — kullanıcı ve kupon
        // durumları da aynı DTO'yu kullanıyor. Aynı şekilli üçüncü bir DTO
        // yazmak kopya kod olurdu.
        [Authorize(Roles = "admin")]
        [HttpPut("{id}/durum")]
        public async Task<IActionResult> ToggleDurum(int id, [FromBody] StatusToggleDto dto)
        {
            var urun = await _context.Products.FindAsync(id);

            if (urun == null)
            {
                return NotFound(new { mesaj = "Ürün bulunamadı!" });
            }

            urun.IsActive = dto.IsActive;
            await _context.SaveChangesAsync();

            // isActive'i geri döndürüyoruz ki panel kendi state'ini
            // tahmin etmek yerine sunucunun söylediğine göre güncellesin.
            return Ok(new
            {
                mesaj = dto.IsActive
                    ? "Ürün satışa açıldı."
                    : "Ürün satıştan kaldırıldı.",
                isActive = urun.IsActive
            });
        }


        // 🔴 DELETE /api/products/5
        [Authorize(Roles = "admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            var product = await _context.Products.FindAsync(id);

            if (product == null)
            {
                return NotFound(new { mesaj = "Silinecek ürün zaten yok!" });
            }

            // Ürünün resimlerini hem diskten hem veritabanından temizle
            var resimler = await _context.ProductImages
                .Where(r => r.ProductId == id)
                .ToListAsync();

            foreach (var resim in resimler)
            {
                DiskDosyasiniSil(resim.Url);
            }

            _context.ProductImages.RemoveRange(resimler);
            _context.Products.Remove(product);

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Ürün silindi biladerim!" });
        }

        // ==========================================================
        //  RESİM ENDPOINT'LERİ
        // ==========================================================

        // 🔴 POST /api/products/5/images   (multipart/form-data, alan adı: dosya)
        [Authorize(Roles = "admin")]
        [HttpPost("{id}/images")]
        public async Task<IActionResult> UploadImage(int id, [FromForm] IFormFile dosya)
        {
            // 1) Ürün var mı?
            var urunVarMi = await _context.Products.AnyAsync(p => p.Id == id);

            if (!urunVarMi)
            {
                return NotFound(new { mesaj = "Ürün bulunamadı biladerim!" });
            }

            // 2) Dosya geldi mi?
            if (dosya == null || dosya.Length == 0)
            {
                return BadRequest(new { mesaj = "Dosya seçilmedi!" });
            }

            // 3) Boyut kontrolü
            if (dosya.Length > MaxDosyaBoyutu)
            {
                return BadRequest(new { mesaj = "Dosya en fazla 5 MB olabilir!" });
            }

            // 4) Uzantı kontrolü — kullanıcı .exe yüklemesin
            var uzanti = Path.GetExtension(dosya.FileName).ToLowerInvariant();

            if (!IzinliUzantilar.Contains(uzanti))
            {
                return BadRequest(new { mesaj = "Sadece jpg, jpeg, png ve webp yüklenebilir!" });
            }

            // 5) İçerik tipi kontrolü — uzantıyı değiştirip kandırmasın
            if (!IzinliTipler.Contains(dosya.ContentType.ToLowerInvariant()))
            {
                return BadRequest(new { mesaj = "Geçersiz dosya tipi!" });
            }

            // 5.5) İÇERİK kontrolü — uzantı ve ContentType yalan söyleyebilir, byte'lar söyleyemez
            if (!await GercektenResimMi(dosya))
            {
                return BadRequest(new { mesaj = "Dosya gerçek bir resim değil!" });
            }

            // 6) Klasörü hazırla
            var klasor = Path.Combine(WebKok(), "uploads", "urunler");
            Directory.CreateDirectory(klasor); // varsa dokunmaz

            // 7) BENZERSİZ isim üret.
            //    Kullanıcının gönderdiği ismi ASLA kullanma:
            //    aynı isimli dosya üzerine yazar + "../../" gibi yol saldırısı riski var.
            var yeniAd = Guid.NewGuid().ToString("N") + uzanti;
            var tamYol = Path.Combine(klasor, yeniAd);

            // 8) Diske yaz
            using (var akis = new FileStream(tamYol, FileMode.Create))
            {
                await dosya.CopyToAsync(akis);
            }

            // 9) Veritabanına kaydet
            var mevcutSayi = await _context.ProductImages.CountAsync(r => r.ProductId == id);

            var resim = new ProductImage
            {
                ProductId = id,
                Url = "/uploads/urunler/" + yeniAd,
                IsMain = mevcutSayi == 0,   // ilk yüklenen otomatik ana resim olsun
                SortOrder = mevcutSayi
            };

            _context.ProductImages.Add(resim);
            await _context.SaveChangesAsync();

            return Ok(new ProductImageDto
            {
                Id = resim.Id,
                Url = resim.Url,
                IsMain = resim.IsMain,
                SortOrder = resim.SortOrder
            });
        }



        // ⭐ SSRF KALKANI — hedef adres bizim İÇ AĞIMIZA mı bakıyor?
        // Adresi IP'ye çevirir; çözümlenen TÜM IP'ler public değilse reddeder.
        // (Tek bir iç IP bile varsa gitmeyiz — güvenli taraf.)
        private static async Task<bool> GuvenliUzakAdresMi(Uri adres)
        {
            IPAddress[] ipler;
            try
            {
                // Host'u IP'ye çevir. Host zaten IP ise onu aynen döndürür.
                ipler = await Dns.GetHostAddressesAsync(adres.DnsSafeHost);
            }
            catch
            {
                return false; // çözümlenemeyen adrese hiç gitme
            }

            if (ipler.Length == 0)
            {
                return false;
            }

            foreach (var ip in ipler)
            {
                if (OzelVeyaDahiliMi(ip))
                {
                    return false;
                }
            }

            return true;
        }

        // Verilen IP localhost / iç ağ / özel aralık mı?
        private static bool OzelVeyaDahiliMi(IPAddress ip)
        {
            // "::ffff:192.168.x.x" gibi IPv4-eşlenmiş IPv6 ise sade IPv4'e indir.
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            // localhost (127.x.x.x ve ::1)
            if (IPAddress.IsLoopback(ip))
            {
                return true;
            }

            // IPv6 özel aralıklar
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal) // fe80::/10
                {
                    return true;
                }

                // fc00::/7 — unique local (iç ağ IPv6)
                var v6 = ip.GetAddressBytes();
                if ((v6[0] & 0xFE) == 0xFC)
                {
                    return true;
                }

                return false;
            }

            // IPv4 özel/dahili aralıklar
            var b = ip.GetAddressBytes();

            if (b[0] == 10) // 10.0.0.0/8
            {
                return true;
            }

            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) // 172.16.0.0/12
            {
                return true;
            }

            if (b[0] == 192 && b[1] == 168) // 192.168.0.0/16
            {
                return true;
            }

            // 169.254.0.0/16 — link-local + BULUT METADATA (169.254.169.254)
            if (b[0] == 169 && b[1] == 254)
            {
                return true;
            }

            if (b[0] == 0) // 0.0.0.0/8 ("bu ağ")
            {
                return true;
            }

            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // 100.64.0.0/10 (CGNAT)
            {
                return true;
            }

            return false;
        }




        // 🔴 POST /api/products/5/images/url   (JSON: { "url": "https://..." })
        // Dış adresteki resmi SUNUCUYA indirir, doğrular ve /uploads'a kaydeder.
        // Böylece kaynak link ölse bile bizim resmimiz durur.
        [Authorize(Roles = "admin")]
        [HttpPost("{id}/images/url")]
        public async Task<IActionResult> UploadImageFromUrl(int id, [FromBody] ImageUrlDto dto)
        {
            // 1) Ürün var mı?
            var urunVarMi = await _context.Products.AnyAsync(p => p.Id == id);
            if (!urunVarMi)
            {
                return NotFound(new { mesaj = "Ürün bulunamadı biladerim!" });
            }

            // 2) URL geçerli ve http/https mi? (file://, ftp:// gibi şemaları engelle)
            if (!Uri.TryCreate(dto.Url, UriKind.Absolute, out var adres) ||
                (adres.Scheme != Uri.UriSchemeHttp && adres.Scheme != Uri.UriSchemeHttps))
            {
                return BadRequest(new { mesaj = "Geçersiz URL! Sadece http/https adres verilebilir." });
            }


            // 2.5) ⭐ SSRF KALKANI — hedef iç ağa/localhost'a bakıyorsa gitme.
            // Mesajı bilerek belirsiz tutuyoruz: saldırgana "burası iç adres"
            // ipucu vermeyelim (ağ haritası çıkarmasını kolaylaştırmasın).
            if (!await GuvenliUzakAdresMi(adres))
            {
                return BadRequest(new { mesaj = "Bu adrese izin verilmiyor." });
            }


            byte[] veri;

            try
            {
                // 3) İndir — en fazla 15 saniye bekle, sonra iptal et
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

                var client = _httpFactory.CreateClient("resimIndirici"); // redirect kapalı (SSRF)


                // Bazı siteler "tarayıcı değilsen vermem" diyor → kimlik ekleyelim
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (ETicaretAPI)");

                using var cevap = await client.GetAsync(adres, cts.Token);

                if (!cevap.IsSuccessStatusCode)
                {
                    return BadRequest(new
                    {
                        mesaj = "Resim indirilemedi (sunucu " + (int)cevap.StatusCode + " döndü)."
                    });
                }

                // 4) Boyut ön kontrolü — sunucu Content-Length söylediyse
                if (cevap.Content.Headers.ContentLength > MaxDosyaBoyutu)
                {
                    return BadRequest(new { mesaj = "Resim en fazla 5 MB olabilir!" });
                }

                veri = await cevap.Content.ReadAsByteArrayAsync(cts.Token);
            }
            catch (TaskCanceledException)
            {
                return BadRequest(new { mesaj = "Resim indirme zaman aşımına uğradı." });
            }
            catch (HttpRequestException)
            {
                return BadRequest(new { mesaj = "Resme ulaşılamadı, URL'yi kontrol et." });
            }

            // 5) İndirilen gerçek boyut sınırda mı? (Content-Length yalan olabilir)
            if (veri.Length == 0 || veri.Length > MaxDosyaBoyutu)
            {
                return BadRequest(new { mesaj = "Resim boş ya da 5 MB'tan büyük!" });
            }

            // 6) Byte'lara bak: gerçek resim mi + hangi uzantı?
            var uzanti = ResimUzantisiBul(veri);
            if (uzanti == null)
            {
                return BadRequest(new { mesaj = "Adresteki içerik geçerli bir resim değil (jpg, png, webp)." });
            }

            // 7) Klasörü hazırla, benzersiz isimle diske yaz
            var klasor = Path.Combine(WebKok(), "uploads", "urunler");
            Directory.CreateDirectory(klasor);

            var yeniAd = Guid.NewGuid().ToString("N") + uzanti;
            var tamYol = Path.Combine(klasor, yeniAd);

            await System.IO.File.WriteAllBytesAsync(tamYol, veri);

            // 8) Veritabanına kaydet (dosya yüklemeyle birebir aynı mantık)
            var mevcutSayi = await _context.ProductImages.CountAsync(r => r.ProductId == id);

            var resim = new ProductImage
            {
                ProductId = id,
                Url = "/uploads/urunler/" + yeniAd,
                IsMain = mevcutSayi == 0,   // ilk resim otomatik ana resim
                SortOrder = mevcutSayi
            };

            _context.ProductImages.Add(resim);
            await _context.SaveChangesAsync();

            return Ok(new ProductImageDto
            {
                Id = resim.Id,
                Url = resim.Url,
                IsMain = resim.IsMain,
                SortOrder = resim.SortOrder
            });
        }




        // 🔴 DELETE /api/products/images/12
        [Authorize(Roles = "admin")]
        [HttpDelete("images/{imageId}")]
        public async Task<IActionResult> DeleteImage(int imageId)
        {
            var resim = await _context.ProductImages.FindAsync(imageId);

            if (resim == null)
            {
                return NotFound(new { mesaj = "Resim bulunamadı!" });
            }

            var anaMiydi = resim.IsMain;
            var urunId = resim.ProductId;

            DiskDosyasiniSil(resim.Url);

            _context.ProductImages.Remove(resim);
            await _context.SaveChangesAsync();

            // Silinen ana resimse, kalanlardan ilkini ana yap (ürün resimsiz kalmasın)
            if (anaMiydi)
            {
                var kalan = await _context.ProductImages
                    .Where(r => r.ProductId == urunId)
                    .OrderBy(r => r.SortOrder)
                    .FirstOrDefaultAsync();

                if (kalan != null)
                {
                    kalan.IsMain = true;
                    await _context.SaveChangesAsync();
                }
            }

            return Ok(new { mesaj = "Resim silindi biladerim!" });
        }

        // 🔴 PUT /api/products/images/12/main — bu resmi ana resim yap
        [Authorize(Roles = "admin")]
        [HttpPut("images/{imageId}/main")]
        public async Task<IActionResult> SetMainImage(int imageId)
        {
            var resim = await _context.ProductImages.FindAsync(imageId);

            if (resim == null)
            {
                return NotFound(new { mesaj = "Resim bulunamadı!" });
            }

            // Aynı ürünün diğer resimlerinin ana işaretini kaldır
            var digerleri = await _context.ProductImages
                .Where(r => r.ProductId == resim.ProductId)
                .ToListAsync();

            foreach (var r in digerleri)
            {
                r.IsMain = (r.Id == imageId);
            }

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Ana resim güncellendi biladerim!" });
        }
    }
}