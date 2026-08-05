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
    //    Dashboard = "şu an ne oluyor"      → sabit dönem, özet kutular
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

        // ⭐ YENİ — kritik stok eşiğinin varsayılanı için (4.1)
        private readonly MagazaAyarlari _ayarlar;

        public ReportsController(
            AppDbContext context,
            RaporTarihi tarih,
            MagazaAyarlari ayarlar)                    // ⭐ YENİ
        {
            _context = context;
            _tarih = tarih;
            _ayarlar = ayarlar;                        // ⭐ YENİ
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
        // ⚠️ VARSAYILAN ARTIK -1.
        //
        // Neden 5 değil? Çünkü "5" iki anlama gelebiliyordu:
        // "kullanıcı 5 istedi" veya "kullanıcı bir şey istemedi".
        // İkisini ayırt edemiyorduk.
        //
        // -1 geçersiz bir eşik olduğu için "istek yok" demenin
        // net yolu. O durumda mağaza ayarındaki eşiği kullanıyoruz
        // — yani rapor, dashboard ve ürün listesi AYNI sayıyı
        // görüyor.
        public async Task<IActionResult> KritikStok([FromQuery] int esik = -1)
        {
            // İstek gelmediyse mağaza ayarına düş
            if (esik < 0)
            {
                esik = _ayarlar.StokAzEsigi;
            }

            if (esik < 1)
            {
                esik = 1;
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


        // ============================================================
        //  🔴 GET /api/admin/reports/iptaller?baslangic=&bitis=
        //
        //  İptal edilen siparişler, sebep dağılımı, kaybedilen ciro.
        //
        //  ⚠️ NEDEN GecerliSiparisler() KULLANMIYORUZ?
        //  O yardımcı "iptal olmayanları" getiriyor — bu raporun tam
        //  tersi. Aynı yardımcıya "iptalleri de getir" parametresi
        //  eklemek onu iki işi birden yapan bulanık bir şeye çevirirdi.
        //  Bir metodun tek bir işi olmalı.
        // ============================================================
        [HttpGet("iptaller")]
        public async Task<IActionResult> Iptaller(
            [FromQuery] DateTime? baslangic,
            [FromQuery] DateTime? bitis)
        {
            var aralik = _tarih.Aralik(baslangic, bitis);

            // ⚠️ HANGİ TARİHE GÖRE FİLTRELİYORUZ?
            //
            // İki aday var:
            //   CreatedAt   → "bu ay verilen siparişlerden kaçı iptal oldu"
            //   CancelledAt → "bu ay kaç iptal işlemi yapıldı"
            //
            // İkincisini seçtik. Sebep: admin bu rapora "bu ay ne kadar
            // ciro kaybettim" sorusuyla bakar; kaybın gerçekleştiği an
            // iptal anıdır. Ocak'ta verilip Şubat'ta iptal edilen sipariş
            // Şubat'ın kaybıdır.
            //
            // ?? CreatedAt: CancelledAt alanı eklenmeden önce iptal
            // edilmiş eski kayıtlarda null. Onları tamamen kaybetmek
            // yerine sipariş tarihine düşürüyoruz. EF bunu SQL'de
            // ISNULL(CancelledAt, CreatedAt) haline getiriyor.
            var iptaller = await _context.Orders
                .Where(o => o.Status == "iptal"
                         && (o.CancelledAt ?? o.CreatedAt) >= aralik.BaslangicUtc
                         && (o.CancelledAt ?? o.CreatedAt) < aralik.BitisUtcHaric)
                .OrderByDescending(o => o.CancelledAt ?? o.CreatedAt)
                .Select(o => new
                {
                    siparisId = o.Id,
                    siparisNo = o.OrderNumber,
                    musteri = o.ShippingFullName,   // dondurulmuş ad
                    tutar = o.Total,
                    sebep = o.CancelReason,
                    siparisTarihi = o.CreatedAt,
                    iptalTarihi = o.CancelledAt
                })
                .ToListAsync();

            // ---- Sebep dağılımı (bellekte) ----
            //
            // Neden SQL'de GroupBy yapmadık? Veri zaten elimizde;
            // ikinci bir sorgu atmak boşuna gidiş-dönüş olurdu.
            // İptal sayısı doğası gereği küçüktür.
            var sebepDagilimi = iptaller
                .GroupBy(x => string.IsNullOrWhiteSpace(x.sebep)
                    ? "Belirtilmemiş"    // null'ı ekranda "null" diye
                    : x.sebep!)          // göstermek amatörce durur
                .Select(g => new
                {
                    sebep = g.Key,
                    adet = g.Count(),
                    tutar = g.Sum(x => x.tutar)
                })
                .OrderByDescending(x => x.adet)
                .ToList();

            // ---- İptal ORANI ----
            //
            // Ham sayı tek başına anlamsız: "12 iptal" iyi mi kötü mü?
            // 1000 siparişte 12 mükemmel, 30 siparişte 12 felaket.
            // Oran olmadan rapor karar verdirmez.
            var toplamSiparis = await _context.Orders
                .CountAsync(o => o.CreatedAt >= aralik.BaslangicUtc
                              && o.CreatedAt < aralik.BitisUtcHaric);

            return Ok(new
            {
                baslangic = aralik.BaslangicYerel.ToString("yyyy-MM-dd"),
                bitis = aralik.BitisYerel.ToString("yyyy-MM-dd"),

                iptalSayisi = iptaller.Count,
                donemToplamSiparis = toplamSiparis,

                // Sıfıra bölme kontrolü: hiç sipariş yoksa oran 0.
                iptalOrani = toplamSiparis > 0
                    ? Math.Round((decimal)iptaller.Count / toplamSiparis * 100, 1)
                    : 0,

                kaybedilenCiro = iptaller.Sum(x => x.tutar),

                sebepler = sebepDagilimi,
                siparisler = iptaller
            });
        }


        // ============================================================
        //  🔴 GET /api/admin/reports/yorumlar?baslangic=&bitis=&maxPuan=2
        //
        //  Düşük puanlı yorumlar — moderasyon ve ürün kalitesi için.
        // ============================================================
        [HttpGet("yorumlar")]
        public async Task<IActionResult> Yorumlar(
            [FromQuery] DateTime? baslangic,
            [FromQuery] DateTime? bitis,
            [FromQuery] int maxPuan = 2)
        {
            var aralik = _tarih.Aralik(baslangic, bitis);

            // Eşiği makul aralıkta tut. 5 verilirse tüm yorumlar döner
            // ve rapor "sorunlu yorumlar" olmaktan çıkar; ama tamamen
            // yasaklamıyoruz — admin bazen hepsine bakmak isteyebilir.
            if (maxPuan < 1)
            {
                maxPuan = 1;
            }

            if (maxPuan > 5)
            {
                maxPuan = 5;
            }

            // ⚠️ BURADA IsHidden FİLTRESİ YOK — bilerek.
            //
            // Müşteri tarafında gizli yorumları saklıyoruz (ReviewsController).
            // Ama bu ADMIN raporu: gizlediği yorumu bir daha göremezse
            // yanlışlıkla gizlediğini geri alamaz. Gizleme "silme" değil;
            // yönetici için görünür kalmalı.
            //
            // Bunun yerine her satıra "gizli" bayrağı koyuyoruz, ön yüz
            // farklı renkte gösterecek.
            var yorumlar = await _context.Reviews
                .Where(r => r.Rating <= maxPuan
                         && r.CreatedAt >= aralik.BaslangicUtc
                         && r.CreatedAt < aralik.BitisUtcHaric)
                .Join(_context.Users,
                      r => r.UserId,
                      u => u.Id,
                      (r, u) => new { r, MusteriAdi = u.FullName })
                .Join(_context.Products,
                      x => x.r.ProductId,
                      p => p.Id,
                      (x, p) => new
                      {
                          yorumId = x.r.Id,
                          urunId = p.Id,
                          urunAdi = p.Name,
                          musteri = x.MusteriAdi,
                          puan = x.r.Rating,
                          yorum = x.r.Comment,
                          tarih = x.r.CreatedAt,
                          gizli = x.r.IsHidden
                      })
                .OrderBy(x => x.puan)              // en kötüler üstte
                .ThenByDescending(x => x.tarih)    // eşitse en yeni üstte
                .ToListAsync();

            return Ok(new
            {
                baslangic = aralik.BaslangicYerel.ToString("yyyy-MM-dd"),
                bitis = aralik.BitisYerel.ToString("yyyy-MM-dd"),
                maxPuan,
                toplam = yorumlar.Count,
                gizliSayisi = yorumlar.Count(x => x.gizli),
                yorumlar
            });
        }


        // ============================================================
        //  🔴 GET /api/admin/reports/musteriler?baslangic=&bitis=
        //
        //  En çok harcayanlar, sipariş sayısı, ortalama sepet.
        // ============================================================
        [HttpGet("musteriler")]
        public async Task<IActionResult> Musteriler(
            [FromQuery] DateTime? baslangic,
            [FromQuery] DateTime? bitis)
        {
            var aralik = _tarih.Aralik(baslangic, bitis);

            var ham = await GecerliSiparisler(aralik)
                .GroupBy(o => o.UserId)
                .Select(g => new
                {
                    userId = g.Key,
                    siparisSayisi = g.Count(),

                    // ⭐ DEĞİŞTİ — kargo hariç.
                    //
                    // "En çok harcayan müşteri" sıralamasında kargo
                    // ücretini saymak yanıltıcı olurdu: 10 kez küçük
                    // sipariş veren biri (10 × 49,90 kargo) tek büyük
                    // sipariş verenden daha "değerli" görünürdü.
                    // Halbuki o kargo parası mağazada kalmıyor.
                    toplamHarcama = g.Sum(o => o.Total - o.ShippingCost),

                    // ⭐ DEĞİŞTİ — kargo hariç.
                    // "Ortalama sepet" bir SATIŞ metriğidir; kargo
                    // sabit bir maliyet kalemi, sepetin büyüklüğü
                    // hakkında bilgi vermez.
                    //
                    // Ortalamayı SQL'e hesaplatıyoruz — AVG fonksiyonu
                    // zaten var, bellekte tekrar bölmeye gerek yok.
                    ortalamaSepet = g.Average(o => o.Total - o.ShippingCost),

                    sonSiparis = g.Max(o => o.CreatedAt)
                })
                .OrderByDescending(x => x.toplamHarcama)
                .Take(50)
                .ToListAsync();

            // ⚠️ NEDEN İKİNCİ BİR SORGU, NEDEN JOIN DEĞİL?
            //
            // GroupBy'ın içine Users join'i sokmak EF'te çirkin bir
            // sorgu üretir (gruplama anahtarına ad da eklemek gerekir
            // ve ad değişirse gruplama bölünür — satışlar raporunda
            // anlattığımız tuzağın aynısı).
            //
            // Bunun yerine 50 kullanıcıyı tek sorguda çekip bellekte
            // eşleştiriyoruz. Bu bir N+1 DEĞİL: 50 sorgu değil, 1 sorgu.
            var idler = ham.Select(x => x.userId).ToList();

            var kullanicilar = await _context.Users
                .Where(u => idler.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.Email })
                .ToListAsync();

            var satirlar = ham.Select(x =>
            {
                var k = kullanicilar.FirstOrDefault(u => u.Id == x.userId);

                return new
                {
                    x.userId,

                    // Hesabı kapatılmış kullanıcı anonimleştirildiği için
                    // adı zaten maskelenmiş gelir. Kullanıcı hiç yoksa
                    // (teorik) okunabilir bir metin bırakıyoruz.
                    musteri = k?.FullName ?? "Bilinmiyor",
                    email = k?.Email ?? "",

                    x.siparisSayisi,
                    x.toplamHarcama,
                    ortalamaSepet = Math.Round(x.ortalamaSepet, 2),
                    x.sonSiparis
                };
            }).ToList();

            return Ok(new
            {
                baslangic = aralik.BaslangicYerel.ToString("yyyy-MM-dd"),
                bitis = aralik.BitisYerel.ToString("yyyy-MM-dd"),
                musteriSayisi = satirlar.Count,
                toplamCiro = satirlar.Sum(x => x.toplamHarcama),
                musteriler = satirlar
            });
        }


        // ============================================================
        //  🔴 GET /api/admin/reports/odemeler?baslangic=&bitis=
        //
        //  Başarılı/başarısız ödeme oranı.
        //
        //  ⚠️ Ödeme şu an SİMÜLE ediliyor, gerçek PSP yok. Bu rapor
        //  bugün çok az şey söylüyor — ama gerçek ödeme entegrasyonu
        //  geldiğinde (Faz 2) en kritik rapor bu olacak: başarısız
        //  ödeme oranının yükselmesi kaybedilen satış demektir.
        //  Yapıyı şimdi kurmak, o gün sıfırdan yazmaktan ucuz.
        // ============================================================
        [HttpGet("odemeler")]
        public async Task<IActionResult> Odemeler(
            [FromQuery] DateTime? baslangic,
            [FromQuery] DateTime? bitis)
        {
            var aralik = _tarih.Aralik(baslangic, bitis);

            // ⚠️ Burada Orders değil Payments filtreleniyor ve tarih
            // alanı PaidAt. Ödeme ile sipariş aynı anda oluşuyor ama
            // ilerideki iade kayıtları farklı tarihli olacak.
            var ozet = await _context.Payments
                .Where(p => p.PaidAt >= aralik.BaslangicUtc
                         && p.PaidAt < aralik.BitisUtcHaric)
                .GroupBy(p => p.Status)
                .Select(g => new
                {
                    durum = g.Key,
                    adet = g.Count(),
                    tutar = g.Sum(x => x.Amount)
                })
                .ToListAsync();

            var toplamAdet = ozet.Sum(x => x.adet);

            var basarili = ozet.FirstOrDefault(x => x.durum == "basarili");

            return Ok(new
            {
                baslangic = aralik.BaslangicYerel.ToString("yyyy-MM-dd"),
                bitis = aralik.BitisYerel.ToString("yyyy-MM-dd"),

                toplamIslem = toplamAdet,

                basariliAdet = basarili?.adet ?? 0,
                basariliTutar = basarili?.tutar ?? 0,

                basariOrani = toplamAdet > 0
                    ? Math.Round((decimal)(basarili?.adet ?? 0) / toplamAdet * 100, 1)
                    : 0,

                // Durum bazlı kırılım — ileride "3D doğrulanamadı",
                // "yetersiz bakiye" gibi durumlar eklenince bu tablo
                // kendiliğinden zenginleşecek.
                durumlar = ozet.OrderByDescending(x => x.adet)
            });
        }


        // ============================================================
        //  🔴 GET /api/admin/reports/kuponlar?baslangic=&bitis=
        //
        //  Kupon başına: kaç kullanım, ne kadar indirim, ne kadar ciro.
        //
        //  ⭐ ASIL SORU: "Bu kampanya kâr getirdi mi?"
        //  İndirim tek başına maliyettir; getirdiği ciroyla birlikte
        //  bakılmadan anlamsızdır. 5.000 TL indirim verip 80.000 TL
        //  ciro geldiyse iyi; 5.000 TL indirim verip 6.000 TL ciro
        //  geldiyse kampanya zarardadır.
        // ============================================================
        [HttpGet("kuponlar")]
        public async Task<IActionResult> Kuponlar(
            [FromQuery] DateTime? baslangic,
            [FromQuery] DateTime? bitis)
        {
            var aralik = _tarih.Aralik(baslangic, bitis);

            // CouponUsage bir OLAY kaydı — "şu kupon şu an kullanıldı".
            // Filtre UsedAt'e göre, kuponun oluşturulma tarihine göre
            // değil: bu dönemde hangi kuponlar kullanıldı sorusu.
            //
            // Orders join'i "getirilen ciro" için gerekli: kullanımın
            // hangi siparişe bağlı olduğunu ve o siparişin tutarını
            // öğreniyoruz. İptal edilenler GecerliSiparisler sayesinde
            // dışarıda kalıyor — iptal olmuş bir siparişin cirosunu
            // kampanyaya yazmak yanlış olurdu.
            var ham = await _context.CouponUsages
                .Where(cu => cu.UsedAt >= aralik.BaslangicUtc
                          && cu.UsedAt < aralik.BitisUtcHaric)
                .Join(GecerliSiparisler(aralik),
                      cu => cu.OrderId,
                      o => o.Id,
                      (cu, o) => new { cu, o })
                .GroupBy(x => x.cu.CouponId)
                .Select(g => new
                {
                    couponId = g.Key,
                    kullanimSayisi = g.Count(),

                    // Toplam verilen indirim — CouponUsage'daki
                    // DONDURULMUŞ tutardan. Kuponun tanımı sonradan
                    // değişse bile o gün ne indirim verildiği sabit.
                    toplamIndirim = g.Sum(x => x.cu.DiscountAmount),

                    // ⭐ DEĞİŞTİ — kargo hariç.
                    //
                    // Getirilen ciro — indirim sonrası, kargo öncesi.
                    // Bir kampanyanın başarısı ürün satışıyla ölçülür.
                    // Kargo ücretini dahil etseydik, ücretsiz kargo
                    // eşiğinin altında kalan siparişler kampanyayı
                    // olduğundan başarılı gösterirdi — üstelik o para
                    // kargo firmasına gidiyor.
                    getirilenCiro = g.Sum(x => x.o.Total - x.o.ShippingCost),

                    // Kaç FARKLI müşteri kullandı?
                    //
                    // Kullanım sayısından farklı bir bilgi: aynı kişi
                    // 5 kez kullandıysa kampanya kitleye ulaşmamış,
                    // 5 farklı kişi kullandıysa ulaşmış demektir.
                    farkliMusteri = g.Select(x => x.cu.UserId).Distinct().Count()
                })
                .ToListAsync();

            // Kupon kodlarını ayrı sorguda alıyoruz (müşteriler
            // raporundaki desenin aynısı — gruplama anahtarına metin
            // koymamak için).
            var idler = ham.Select(x => x.couponId).ToList();

            var kuponlar = await _context.Coupons
                .Where(c => idler.Contains(c.Id))
                .Select(c => new { c.Id, c.Code, c.Description, c.DiscountType, c.DiscountValue })
                .ToListAsync();

            var satirlar = ham.Select(x =>
            {
                var k = kuponlar.FirstOrDefault(c => c.Id == x.couponId);

                return new
                {
                    x.couponId,

                    // Kupon silinmişse kod bilinmiyor. CouponUsage
                    // kaydı duruyor ama tanımı gitmiş — bu, kupon
                    // koduna da dondurma uygulamamız gerektiğini
                    // gösteren bir işaret (ileride ele alınabilir).
                    kod = k?.Code ?? "(silinmiş kupon)",
                    aciklama = k?.Description ?? "",

                    x.kullanimSayisi,
                    x.farkliMusteri,
                    x.toplamIndirim,
                    x.getirilenCiro,

                    // ⭐ VERİMLİLİK: 1 TL indirim başına kaç TL ciro?
                    //
                    // Kampanyaları kıyaslamanın en sade yolu. 10'un
                    // üstü genelde iyi, 2'nin altı sorgulanmalı.
                    // Ham sayılar tek başına kıyaslanamaz: küçük kupon
                    // az indirim az ciro getirir, oran ise ölçekten
                    // bağımsızdır.
                    verimlilik = x.toplamIndirim > 0
                        ? Math.Round(x.getirilenCiro / x.toplamIndirim, 1)
                        : (decimal?)null
                };
            })
            .OrderByDescending(x => x.getirilenCiro)
            .ToList();

            return Ok(new
            {
                baslangic = aralik.BaslangicYerel.ToString("yyyy-MM-dd"),
                bitis = aralik.BitisYerel.ToString("yyyy-MM-dd"),
                kuponSayisi = satirlar.Count,
                toplamKullanim = satirlar.Sum(x => x.kullanimSayisi),
                toplamIndirim = satirlar.Sum(x => x.toplamIndirim),
                toplamCiro = satirlar.Sum(x => x.getirilenCiro),
                kuponlar = satirlar
            });
        }
    }
}