using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Services;

namespace ETicaretAPI.Controllers
{
    // ============================================================
    //  RAPORLAR
    //
    //  NEDEN AYRI CONTROLLER, NEDEN AdminController'A EKLEMEDİK?
    //  AdminController zaten 600+ satır. Dokuz rapor daha eklenince
    //  1000 satırı geçerdi ve içinde bir şey bulmak imkânsızlaşırdı.
    //
    //  Ayrım keyfi değil, SORUMLULUĞA göre: AdminController "yönet"
    //  (kullanıcı rolü değiştir, istatistik göster), ReportsController
    //  "analiz et" (geçmişe bak, para hesapla). İki farklı iş.
    //
    //  DASHBOARD'DAN FARKI NE?
    //    Dashboard = "şu an ne oluyor"     → sabit dönem, özet kutular
    //    Raporlar  = "şu tarihler arasında  → seçilen aralık, tablo,
    //                 ne oldu"                Excel'e aktarılabilir
    //
    //  Bu ayrım konmazsa ikinci bir dashboard ortaya çıkar ve
    //  kullanıcı hangisine bakacağını bilemez.
    //
    //  YETKİ:
    //  Sınıf seviyesinde [Authorize(Roles = "admin")] — tüm uçlar
    //  kapalı. Tek tek yazmak yerine sınıfa koymak, yeni bir endpoint
    //  eklerken yetki koymayı UNUTMAYI imkânsız kılar.
    //  Üç katmanlı yetkinin üçüncü ve tek gerçek katmanı burası.
    // ============================================================
    [Route("api/admin/reports")]
    [ApiController]
    [Authorize(Roles = "admin")]
    public class ReportsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly RaporTarihi _tarih;

        public ReportsController(AppDbContext context, RaporTarihi tarih)
        {
            _context = context;
            _tarih = tarih;
        }


        // ============================================================
        //  ORTAK YARDIMCI: iptal edilmemiş siparişler
        //
        //  NEDEN AYRI METOT?
        //  Her ciro/kâr raporunda "iptal edilenleri sayma" kuralı
        //  geçerli. Dokuz yere kopyalasaydık biri unutulur ve o rapor
        //  sessizce şişik ciro gösterirdi.
        //
        //  IQueryable döndürüyoruz, List değil. Fark kritik:
        //    IQueryable → henüz veritabanına GİTMEDİ, üstüne filtre
        //                 eklenebilir, hepsi tek SQL'e derlenir
        //    List       → veri zaten çekildi, geri dönüş yok
        //
        //  Yani çağıran taraf ".Where(...)" ekleyebiliyor ve bu
        //  filtre SQL'e gömülüyor — tüm siparişleri belleğe çekip
        //  elemiyoruz.
        // ============================================================
        private IQueryable<Models.Order> GecerliSiparisler(RaporAraligi aralik)
        {
            return _context.Orders
                .Where(o => o.Status != "iptal"
                         && o.CreatedAt >= aralik.BaslangicUtc
                         && o.CreatedAt < aralik.BitisUtcHaric);
        }


        // ============================================================
        //  🔴 GET /api/admin/reports/satislar?baslangic=&bitis=
        //
        //  En çok satanlar: adet, ciro, maliyet, KÂR ve marj.
        //  Bu raporun tamamı dün eklediğimiz UnitCost/ProductName
        //  alanları sayesinde mümkün.
        // ============================================================
        [HttpGet("satislar")]
        public async Task<IActionResult> Satislar(
            [FromQuery] DateTime? baslangic,
            [FromQuery] DateTime? bitis)
        {
            var aralik = _tarih.Aralik(baslangic, bitis);

            // ⭐ Join ile sipariş kalemlerini siparişlere bağlıyoruz.
            //
            // Neden gerekli? Kalemde tarih yok, sipariş tarihine göre
            // filtreleyeceğiz. Ayrıca iptal edilmiş siparişlerin
            // kalemleri dışarıda kalmalı.
            //
            // ⚠️ Buradaki Join meşru — dün kaldırdıklarımızdan farklı.
            // Onlar "adı canlı oku" amaçlıydı (dondurma ihlali).
            // Bu ise gerçek bir ilişki sorgusu: kalem hangi siparişe
            // ait, o sipariş ne zaman verilmiş?
            var ham = await _context.OrderItems
                .Join(GecerliSiparisler(aralik),
                      oi => oi.OrderId,
                      o => o.Id,
                      (oi, o) => oi)

                // ⚠️ SADECE ProductId'ye göre gruplandık, ProductName'e
                // GÖRE DEĞİL.
                //
                // Neden? Ürün adı zaman içinde değişmiş olabilir
                // ("Kulaklık" → "Kablosuz Kulaklık"). Adı da gruplama
                // anahtarına koysaydık aynı ürün raporda İKİ SATIR
                // olarak görünürdü ve toplamlar bölünürdü.
                //
                // Kimlik ProductId'dir; ad sadece bir etikettir.
                .GroupBy(oi => oi.ProductId)

                .Select(g => new
                {
                    urunId = g.Key,

                    // Adı gruptan seçiyoruz. Max = alfabetik en büyük.
                    // Keyfi görünüyor ama tutarlı: aynı ürün her rapor
                    // çalıştırmasında aynı adla görünür. Alternatif "en
                    // son siparişteki ad" olurdu; SQL'de alt sorgu
                    // gerektirir ve kazancı yok — ad zaten nadiren
                    // değişiyor.
                    urunAdi = g.Max(x => x.ProductName),

                    adet = g.Sum(x => x.Quantity),

                    ciro = g.Sum(x => x.Quantity * x.UnitPrice),

                    // ⭐ MALİYET — dondurulmuş UnitCost'tan.
                    //
                    // UnitCost nullable. null olanları 0 sayıp
                    // topluyoruz ama bu "maliyeti sıfır" demek DEĞİL —
                    // aşağıdaki sayaç kaç kalemin bilinmediğini ayrıca
                    // tutuyor, kâr o zaman gösterilmiyor.
                    maliyet = g.Sum(x => x.UnitCost.HasValue
                        ? x.Quantity * x.UnitCost.Value
                        : 0),

                    // ⭐ Maliyeti bilinmeyen kalem sayısı.
                    //
                    // Bu alan olmasaydı rapor, maliyeti girilmemiş
                    // ürünler için "kâr = ciro" gösterirdi — yani
                    // %100 marj. Uydurma bir rakam.
                    //
                    // Bunun yerine "bilinmiyor" diyeceğiz. Yanlış sayı,
                    // eksik sayıdan tehlikelidir.
                    maliyetBilinmeyen = g.Count(x => x.UnitCost == null)
                })
                .OrderByDescending(x => x.ciro)
                .Take(50)
                .ToListAsync();

            // ---- Kâr ve marjı BELLEKTE hesaplıyoruz ----
            //
            // Neden SQL'de değil? Koşullu mantık (bilinmiyorsa null
            // döndür) ve sıfıra bölme kontrolü SQL'de okunmaz hale
            // gelirdi. Veri zaten 50 satır — bellekte hesaplamanın
            // maliyeti sıfıra yakın.
            var satirlar = ham.Select(x =>
            {
                // Maliyeti eksik olan varsa kâr HESAPLANMAZ.
                bool maliyetTam = x.maliyetBilinmeyen == 0;

                decimal? kar = maliyetTam ? x.ciro - x.maliyet : null;

                // Marj = kâr / ciro × 100
                //
                // ⚠️ ciro 0 olabilir mi? Teorik olarak evet (fiyatı 0
                // olan ürün). Sıfıra bölme çalışma zamanı hatası
                // fırlatır — kontrol şart.
                decimal? marj = (maliyetTam && x.ciro > 0)
                    ? Math.Round(kar!.Value / x.ciro * 100, 1)
                    : null;

                return new
                {
                    x.urunId,
                    x.urunAdi,
                    x.adet,
                    x.ciro,
                    maliyet = maliyetTam ? x.maliyet : (decimal?)null,
                    kar,
                    marj,

                    // Ön yüz bu bayrağa bakıp "—" veya uyarı ikonu
                    // gösterecek. Boş hücre "veri yok" mu "sıfır" mı
                    // belli olmaz; açık bir bayrak belirsizliği kaldırır.
                    maliyetEksik = !maliyetTam
                };
            }).ToList();

            return Ok(new
            {
                // Aralığı cevaba koyuyoruz: kullanıcı parametre
                // göndermediyse hangi dönemi gördüğünü bilmeli.
                // "Son 30 gün" yazısı ön yüzde uydurulmamalı, sunucudan
                // gelmeli — tek doğru kaynak.
                baslangic = aralik.BaslangicYerel.ToString("yyyy-MM-dd"),
                bitis = aralik.BitisYerel.ToString("yyyy-MM-dd"),

                // Genel toplamlar — tablo altındaki özet satırı için.
                toplamCiro = satirlar.Sum(x => x.ciro),
                toplamAdet = satirlar.Sum(x => x.adet),

                // ⚠️ Toplam kâr, SADECE maliyeti bilinen satırlardan.
                // Karışık toplama yapmak "kısmen doğru" bir sayı üretir
                // ki bu en kötüsüdür.
                toplamKar = satirlar.Where(x => !x.maliyetEksik).Sum(x => x.kar ?? 0),
                maliyetEksikSatirSayisi = satirlar.Count(x => x.maliyetEksik),

                urunler = satirlar
            });
        }


        // ============================================================
        //  🔴 GET /api/admin/reports/kategoriler?baslangic=&bitis=
        //
        //  Kategori bazlı ciro dağılımı — pasta grafiği için.
        // ============================================================
        [HttpGet("kategoriler")]
        public async Task<IActionResult> Kategoriler(
            [FromQuery] DateTime? baslangic,
            [FromQuery] DateTime? bitis)
        {
            var aralik = _tarih.Aralik(baslangic, bitis);

            // ⚠️ BURADA Products JOIN'İ ZORUNLU.
            //
            // Kategori bilgisi sipariş kalemine DONDURULMADI. Neden
            // dondurmadık? Çünkü kategori bir SINIFLANDIRMADIR, bir
            // sözleşme değil. Ürün "Elektronik"ten "Bilgisayar"a
            // taşınırsa, geçmiş raporun da yeni sınıflandırmayı
            // kullanması genelde İSTENEN davranıştır — "bu ürün bugün
            // hangi rafta" sorusunun cevabı bugünkü raftır.
            //
            // Fiyat ve ad farklıydı: onlar müşteriyle yapılan
            // sözleşmenin parçası, değişmemeleri gerekiyordu.
            //
            // ⚠️ Bunun bedeli: silinen ürünlerin kalemleri bu raporda
            // görünmez (INNER JOIN düşürür). Kabul ediyoruz — kategori
            // raporu bir eğilim raporudur, kuruş hassasiyeti aranmaz.
            var ham = await _context.OrderItems
                .Join(GecerliSiparisler(aralik),
                      oi => oi.OrderId,
                      o => o.Id,
                      (oi, o) => oi)
                .Join(_context.Products,
                      oi => oi.ProductId,
                      p => p.Id,
                      (oi, p) => new { oi, p.CategoryId })
                .Join(_context.Categories,
                      x => x.CategoryId,
                      c => c.Id,
                      (x, c) => new { x.oi, KategoriAdi = c.Name, KategoriId = c.Id })
                .GroupBy(x => new { x.KategoriId, x.KategoriAdi })
                .Select(g => new
                {
                    kategoriId = g.Key.KategoriId,
                    kategoriAdi = g.Key.KategoriAdi,
                    adet = g.Sum(x => x.oi.Quantity),
                    ciro = g.Sum(x => x.oi.Quantity * x.oi.UnitPrice)
                })
                .OrderByDescending(x => x.ciro)
                .ToListAsync();

            var genelToplam = ham.Sum(x => x.ciro);

            // Yüzdeyi bellekte hesaplıyoruz: genel toplamı bilmek için
            // önce tüm grupların gelmesi gerekiyordu. SQL'de yapmak
            // pencere fonksiyonu (OVER) gerektirirdi — EF LINQ'ta bu
            // zahmetin karşılığı yok.
            var satirlar = ham.Select(x => new
            {
                x.kategoriId,
                x.kategoriAdi,
                x.adet,
                x.ciro,
                yuzde = genelToplam > 0
                    ? Math.Round(x.ciro / genelToplam * 100, 1)
                    : 0
            });

            return Ok(new
            {
                baslangic = aralik.BaslangicYerel.ToString("yyyy-MM-dd"),
                bitis = aralik.BitisYerel.ToString("yyyy-MM-dd"),
                toplamCiro = genelToplam,
                kategoriler = satirlar
            });
        }


        // ============================================================
        //  🔴 GET /api/admin/reports/olu-stok?baslangic=&bitis=
        //
        //  Seçilen aralıkta HİÇ satılmayan aktif ürünler.
        //  "Parası rafta duran" ürünler — indirim/tasfiye kararı için.
        // ============================================================
        [HttpGet("olu-stok")]
        public async Task<IActionResult> OluStok(
            [FromQuery] DateTime? baslangic,
            [FromQuery] DateTime? bitis)
        {
            var aralik = _tarih.Aralik(baslangic, bitis);

            // 1) Aralıkta satılan ürün id'leri.
            //
            // ⚠️ Distinct() önemli: aynı ürün 50 kez satıldıysa 50
            // satır dönerdi. Bize sadece "satıldı mı" lazım.
            var satilanIdler = await _context.OrderItems
                .Join(GecerliSiparisler(aralik),
                      oi => oi.OrderId,
                      o => o.Id,
                      (oi, o) => oi.ProductId)
                .Distinct()
                .ToListAsync();

            // 2) Aktif olup bu listede olmayanlar.
            //
            // ⚠️ Contains bir C# listesiyle çalışıyor; EF bunu SQL'de
            // "WHERE Id NOT IN (1,2,3,...)" haline getiriyor.
            //
            // Ölçek uyarısı: ürün sayısı binlere çıkarsa bu IN listesi
            // devleşir ve SQL Server parametre sınırına (2100) takılır.
            // O noktada iki sorguyu tek sorguya çevirmek gerekir
            // (LEFT JOIN + IS NULL). Şimdilik okunabilirlik kazanıyor;
            // README'ye not düşülecek.
            var oluStok = await _context.Products
                .Where(p => p.IsActive && !satilanIdler.Contains(p.Id))
                .OrderByDescending(p => p.Stock)
                .Select(p => new
                {
                    urunId = p.Id,
                    urunAdi = p.Name,
                    stok = p.Stock,
                    fiyat = p.Price,

                    // ⭐ Rafta bekleyen para — asıl karar bu sayıya göre
                    // verilir. "20 adet kalmış" bilgisi tek başına
                    // anlamsız; "20 × 800 TL = 16.000 TL beklemede"
                    // anlamlı.
                    //
                    // Maliyet varsa onu kullanıyoruz (gerçek bağlanan
                    // sermaye), yoksa satış fiyatını.
                    bagliSermaye = p.Stock * (p.Cost ?? p.Price),
                    maliyetVarMi = p.Cost != null
                })
                .ToListAsync();

            return Ok(new
            {
                baslangic = aralik.BaslangicYerel.ToString("yyyy-MM-dd"),
                bitis = aralik.BitisYerel.ToString("yyyy-MM-dd"),
                urunSayisi = oluStok.Count,
                toplamBagliSermaye = oluStok.Sum(x => x.bagliSermaye),
                urunler = oluStok
            });
        }


        // ============================================================
        //  🔴 GET /api/admin/reports/kritik-stok?esik=5
        //
        //  Stoğu eşiğin altındaki aktif ürünler — sipariş verme listesi.
        //
        //  ⚠️ TARİH PARAMETRESİ YOK. Bu rapor "şu an" sorusuna cevap
        //  veriyor, geçmişe değil. Stok anlık bir değer; "geçen ayki
        //  stok" diye bir şey kaydetmiyoruz (o Aşama 3'teki
        //  StockMovement işi).
        //
        //  Tutarlılık uğruna kullanılmayan parametre eklemek, onu
        //  gönderen ön yüzü yanıltır.
        // ============================================================
        [HttpGet("kritik-stok")]
        public async Task<IActionResult> KritikStok([FromQuery] int esik = 5)
        {
            // Kullanıcı 0 veya negatif gönderirse rapor anlamsızlaşır.
            // Üst sınır da koyuyoruz: 1000 verilirse tüm katalog döner
            // ve bu bir "kritik stok" raporu olmaktan çıkar.
            if (esik < 1)
            {
                esik = 1;
            }

            if (esik > 100)
            {
                esik = 100;
            }

            var urunler = await _context.Products
                .Where(p => p.IsActive && p.Stock <= esik)
                .OrderBy(p => p.Stock)
                .Select(p => new
                {
                    urunId = p.Id,
                    urunAdi = p.Name,
                    barkod = p.Barcode,
                    stok = p.Stock,
                    fiyat = p.Price,

                    // ⭐ Tükenmiş mi, azalmış mı? İkisi farklı aciliyet.
                    // Ön yüzde renk kararı buna göre verilecek:
                    // tükendi = kırmızı, azaldı = turuncu.
                    // Renk bilgi taşımalı; hepsini aynı yapmak bilgi
                    // kaybıdır.
                    tukendi = p.Stock == 0
                })
                .ToListAsync();

            return Ok(new
            {
                esik,
                tukenenSayisi = urunler.Count(x => x.tukendi),
                urunSayisi = urunler.Count,
                urunler
            });
        }
    }
}