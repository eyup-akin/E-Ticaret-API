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

        // ⭐ YENİ — kombin ve "birlikte alınanlar" önerileri
        private readonly KombinServisi _kombin;


        // ⭐ YENİ (7.4) — ana sayfada bölüm başına ürün sayısı.
        //
        // ⚠️ Beş bölümün beşi de aynı sayıyı kullanıyor; elle beş kez
        // 10 yazsaydık biri değiştiğinde diğerleri sessizce eski
        // kalırdı. Sayı 10: yatay şeritte kaydırmadan 2-3 kart
        // görünüyor, tamamı birkaç hamlede geziliyor. Daha fazlası
        // müşterinin hiç görmeyeceği ürünü indirmek olurdu.
        private const int BolumUrunSayisi = 10;

        // ⭐ YENİ (2026-08-12) — "En Popüler Ürünler" hangi dönemi sayar?
        //
        // ⚠️ Sayı burada, sorgunun içinde değil: pencere bir iş kararı
        // ve ileride mağaza ayarlarına taşınabilir. Sorguya gömülü
        // olsaydı değiştirmek için sorguyu okumak gerekirdi.
        private const int PopulerGunSayisi = 30;

        // Resim yükleme kuralları — tek yerde dursun
        private const long MaxDosyaBoyutu = 5 * 1024 * 1024; // 5 MB
        private static readonly string[] IzinliUzantilar = { ".jpg", ".jpeg", ".png", ".webp" };
        private static readonly string[] IzinliTipler = { "image/jpeg", "image/png", "image/webp" };

        public ProductsController(
            AppDbContext context,
            IWebHostEnvironment env,
            IHttpClientFactory httpFactory,
            StokDefteri defter,              // ⭐ YENİ
            MagazaAyarlari ayarlar,
            KombinServisi kombin)            // ⭐ YENİ
        {
            _context = context;
            _env = env;
            _httpFactory = httpFactory;
            _defter = defter;                // ⭐ YENİ
            _ayarlar = ayarlar;              // ⭐ YENİ
            _kombin = kombin;                // ⭐ YENİ
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
        // ⭐ YENİ (4.8) — hangi ürünler fiziksel olarak silinebilir?
        //
        // ⚠️ ÜRÜN BAŞINA SORGU YOK — toplam ÜÇ sorgu.
        //
        // Projeksiyonun içine Any() yazsaydık EF her satır için ayrı
        // bir korelasyonlu alt sorgu üretirdi: 40 ürünlük listede 120
        // alt sorgu. Bunun yerine üç tabloya da "bu id'lerden hangisi
        // geçiyor?" diye tek seferde soruyoruz. Puan ve favori
        // doldurmadaki desenin aynısı.
        private async Task SilinebilirligiDoldur(List<ProductDto> urunler)
        {
            if (urunler.Count == 0)
            {
                return;
            }

            var idler = urunler.Select(u => u.Id).ToList();

            var siparisliler = await _context.OrderItems
                .Where(oi => idler.Contains(oi.ProductId))
                .Select(oi => oi.ProductId)
                .Distinct()
                .ToListAsync();

            var yorumlular = await _context.Reviews
                .Where(r => idler.Contains(r.ProductId))
                .Select(r => r.ProductId)
                .Distinct()
                .ToListAsync();

            var hareketliler = await _context.StockMovements
                .Where(sm => idler.Contains(sm.ProductId))
                .Select(sm => sm.ProductId)
                .Distinct()
                .ToListAsync();

            // HashSet: aşağıdaki döngüde her ürün için üç arama
            // yapılıyor. Liste üzerinde Contains ararsak arama sayısı
            // ürün × kayıt olurdu.
            var gecmisliler = new HashSet<int>(siparisliler);
            gecmisliler.UnionWith(yorumlular);
            gecmisliler.UnionWith(hareketliler);

            foreach (var urun in urunler)
            {
                urun.SilinebilirMi = !gecmisliler.Contains(urun.Id);
            }
        }


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

        // ⭐ YENİ (6.1) — SIRALAMA BEYAZ LİSTESİ
        //
        // ⚠️ NEDEN SABİT LİSTE, NEDEN GELEN METNİ ALAN ADINA ÇEVİRMİYORUZ?
        //
        // "siralama" istekten geliyor. Gelen metni doğrudan bir sütun
        // adına çevirseydik istemci `?siralama=Cost` yazıp MALİYETE göre
        // sıralatabilirdi. Cost'u JSON'dan siliyoruz ama sıralama onu
        // geri sızdırırdı: listeyi maliyete göre sıralayıp "en pahalıya
        // mal olan ürün hangisi" sorusu cevaplanabilirdi. Veriyi
        // gizlemek yetmez, veriden TÜRETİLEN her sinyali de kapatmak
        // gerekir — gizli yorumların ortalamaya girmemesiyle aynı kural.
        //
        // Listede olmayan her değer sessizce varsayılana düşer; hata
        // dönmüyoruz çünkü bozuk bir sıralama parametresi müşterinin
        // ürün görmesini engellememeli.
        private static readonly string[] GecerliSiralamalar =
        {
            "fiyat_artan", "fiyat_azalan", "yeni", "populer", "puan"
        };


        // ⭐ YENİ (6.1) — VİRGÜLLÜ ID LİSTESİNİ AYRIŞTIR ("1,3,7")
        //
        // Bozuk parçalar sessizce atılıyor: "1,abc,3" → [1, 3]. Hata
        // dönmemenin sebebi yukarıdakiyle aynı — filtre parametresi
        // müşterinin ürün görmesini engelleyen bir şey olmamalı.
        //
        // ⚠️ Distinct() gerekli: "2,2,2" gelirse EF'e üç kopya parametre
        // gitmesinin anlamı yok.
        private static List<int> IdListesiAyristir(string? metin)
        {
            if (string.IsNullOrWhiteSpace(metin))
            {
                return new List<int>();
            }

            return metin
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(parca => int.TryParse(parca, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .Take(50)   // kategori sayısı hiçbir zaman bu kadar olmayacak; sınır kötü niyetli isteğe karşı
                .ToList();
        }


        // ⭐ YENİ (6.1) — FİLTRELERİ KURAN ORTAK YER
        //
        // ⚠️ NEDEN AYRI METOT?
        //
        // İki uç aynı filtre kümesini kullanıyor: liste (GetProducts) ve
        // sayaç (GetUrunSayisi). İkisine ayrı ayrı yazsaydık, sayaç
        // "47 ürün" der ama listeye basınca 52 ürün çıkardı — panelde
        // gösterilen sayı ile listenin çelişmesi, sayının hiç olmamasından
        // kötü olurdu. Filtre TEK yerde kuruluyor.
        //
        // ⚠️ IQueryable döndürüyor, List değil: sorgu henüz veritabanına
        // gitmedi. Çağıran taraf sıralama ve projeksiyon ekliyor, hepsi
        // tek SQL'e derleniyor. (ReportsController.GecerliSiparisler ile
        // aynı desen.)
        //
        // ⚠️ SIRALAMA BURADA YOK — bilerek. Sayaç için sıralama hem
        // gereksiz hem maliyetli: COUNT(*) alırken satırları sıraya
        // dizmenin hiçbir faydası yok.
        private IQueryable<Product> UrunSorgusuKur(
            bool adminMi,
            int? categoryId,
            string? search,
            bool? aktif,
            bool arsiv,
            decimal? minFiyat,
            decimal? maxFiyat,
            string? kategoriler,
            double? minPuan,
            bool sadeceStokta,
            string? idler = null)
        {
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

            // ⭐ YENİ (4.8) — ARŞİV FİLTRESİ
            //
            // ⚠️ Müşteri dalında bu filtreye GEREK YOK ama yine de
            // koyuyoruz. Sebep: arşivleme ürünü zaten pasife çekiyor,
            // yani yukarıdaki IsActive filtresi onu şimdiden eliyor.
            // Ama bu iki kuralın BİRBİRİNE BAĞLI olmasına güvenmek
            // kırılgan olurdu — yarın "arşivle ama satışta bırak"
            // diye bir yol açılırsa arşivli ürünler sessizce vitrine
            // düşerdi. Filtre burada, o bağımlılığı ortadan kaldırıyor.
            //
            // Admin tarafında varsayılan false: arşiv, "gözümün
            // önünden çek" demek.
            //
            // ⚠️ ?arsiv=true "SADECE arşivliler" değil, "arşivliler
            // DE dahil" anlamına geliyor. Sadece-arşiv görünümü
            // seçmedik: admin bir ürünü arşivden çıkarmak için
            // aradığında, onu tanıdığı listede (diğer ürünlerin
            // arasında) bulması daha kolay. Ayrıca arşivli ürünün
            // rozeti zaten onu ayırt ediyor.
            if (!adminMi || !arsiv)
            {
                query = query.Where(p => !p.ArsivlendiMi);
            }

            // ⭐ YENİ (GV/Faz 4) — BELİRLİ ID'LERİ GETİR
            //
            // Mobildeki "Son gezdiğin ürünler" şeridi için. Geçmiş
            // CİHAZDA saklanıyor (sunucuda değil — gerekçesi
            // sonGezilenler.js'te), yani elimizde yalnızca bir id
            // listesi oluyor ve onların güncel hallerini çekmek
            // gerekiyor.
            //
            // ⚠️ NEDEN AYRI BİR UÇ DEĞİL?
            // Çünkü bu ucun yaptığı her şey (görünürlük kilidi,
            // maliyet gizleme, stok türetme, resim ve puan
            // doldurma) orada da aynen gerekli. Ayrı yazsaydık
            // ikinci bir kopya doğar ve biri güncellenip diğeri
            // unutulurdu — üstelik en tehlikeli yerde: müşteriye
            // giden JSON'da.
            //
            // ⚠️ NEDEN ÜRÜN BAŞINA İSTEK DEĞİL?
            // 12 ürün = 12 istek. Yol haritası 7.4'teki "tek
            // endpoint kuralı" tam olarak bunu yasaklıyor.
            //
            // ⚠️ SIRA KORUNMUYOR — bilerek, ve istemci bunu biliyor.
            // SQL'e "şu sırayla getir" demek (CASE WHEN zinciri)
            // hem çirkin hem gereksiz: sırayı zaten İSTEYEN taraf
            // biliyor, gelen listeyi kendi sırasına dizmesi bir
            // satır. Sunucu sırayı bilmiyor, bilmesi de gerekmiyor.
            var idListesi = IdListesiAyristir(idler);

            if (idListesi.Count > 0)
            {
                query = query.Where(p => idListesi.Contains(p.Id));
            }

            // ⭐ DEĞİŞTİ (6.1) — TEK KATEGORİ mi, ÇOKLU KATEGORİ mi?
            //
            // İkisi de AYNI boyutu filtreliyor, o yüzden birlikte
            // uygulanmıyorlar: "kategoriler" doluysa o kazanır.
            //
            // ⚠️ Neden ikisi birden AND'lenmedi? "categoryId=2 &
            // kategoriler=3,5" isteğinde kesişim BOŞ küme olurdu ve
            // ekranda sebepsiz yere "ürün bulunamadı" çıkardı.
            // Neden "categoryId" silinip yerine tek parametre
            // konulmadı? Çünkü admin paneli ve mobildeki kategori
            // ekranı onu kullanıyor; silmek üç katmanda kırılma
            // yaratırdı ve bu aşamanın işi değil.
            var kategoriIdler = IdListesiAyristir(kategoriler);

            if (kategoriIdler.Count > 0)
            {
                query = query.Where(p => kategoriIdler.Contains(p.CategoryId));
            }
            else if (categoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == categoryId.Value);
            }

            // ⭐ DEĞİŞTİ (6.1) — arama artık AÇIKLAMADA da geçiyor.
            //
            // Aşama 2'de eklenen Description arama dışında kalmıştı:
            // "pamuklu" diye arayan müşteri, kelimesi açıklamada geçen
            // ürünü bulamıyordu.
            //
            // ⚠️ Description nullable — null kontrolü şart. EF bunu
            // SQL'e çeviriyor ve SQL'de NULL LIKE '%x%' zaten NULL
            // (yani false) dönerdi, ama kontrolü yazmak niyeti açık
            // ediyor ve sağlayıcı değişirse davranış sabit kalıyor.
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(p =>
                    p.Name.Contains(search) ||
                    (p.Description != null && p.Description.Contains(search)));
            }

            // ⭐ YENİ (6.1) — FİYAT ARALIĞI
            //
            // ⚠️ Fiyatlar KDV DAHİL (Aşama 4.3). Müşteri ekranda hangi
            // sayıyı görüyorsa aralık ona göre süzüyor; matrah üzerinden
            // filtrelemek "500 TL'ye kadar" diyen müşteriye 600 TL'lik
            // ürün gösterirdi.
            //
            // min > max gelirse sonuç boş küme olur. Bunu hata saymıyoruz:
            // istemci kaydırıcıyı öyle bıraktıysa "böyle ürün yok" doğru
            // cevaptır.
            if (minFiyat.HasValue)
            {
                query = query.Where(p => p.Price >= minFiyat.Value);
            }

            if (maxFiyat.HasValue)
            {
                query = query.Where(p => p.Price <= maxFiyat.Value);
            }

            // ⭐ YENİ (6.1) — SADECE STOKTA OLANLAR
            //
            // ⚠️ Ham stok müşteriye gitmiyor ama SÜZMEK sorun değil:
            // "stokta var mı" bilgisi zaten StokDurumu ile gönderiliyor.
            // Sızan bir şey yok, gizlenen sayı yine gizli.
            if (sadeceStokta)
            {
                query = query.Where(p => p.Stock > 0);
            }

            // ⭐ YENİ (6.1) — EN AZ ŞU KADAR PUAN
            //
            // ⚠️ GİZLİ YORUMLAR ORTALAMAYA GİRMİYOR (IsHidden == false).
            // PuanlariDoldur ile AYNI kural — farklı olsaydı listede
            // "4,2 puan" yazan ürün "min 4 puan" filtresinde
            // kaybolabilirdi. Bir kaydı görünürlükten çıkarıyorsan
            // ondan TÜRETİLEN her şeyi de çıkarmalısın.
            //
            // ⚠️ HİÇ YORUMU OLMAYAN ÜRÜN ELENİR — bilerek.
            // Average() boş kümede null döner, null >= 4 ise false.
            // "En az 4 yıldız" diyen müşteri, puanı OLAN ürün istiyor;
            // puansız ürünü listeye koymak "bu ürün 4+ puanlı" demek
            // olurdu ve bu yanlış bir iddia olurdu.
            //
            // (double?) dönüşümü şart: Rating int, cast edilmezse EF
            // boş kümede 0 üretir ve puansız ürünler "0 puan" sayılırdı.
            if (minPuan.HasValue)
            {
                var esik = minPuan.Value;

                query = query.Where(p =>
                    _context.Reviews
                        .Where(r => r.ProductId == p.Id && !r.IsHidden)
                        .Average(r => (double?)r.Rating) >= esik);
            }

            return query;
        }


        // 🟢 GET /api/products?categoryId=2&search=nike&aktif=false
        //    &minFiyat=100&maxFiyat=500&kategoriler=1,3&minPuan=4
        //    &sadeceStokta=true&siralama=fiyat_artan
        [HttpGet]
        public async Task<IActionResult> GetProducts(
            [FromQuery] int? categoryId,
            [FromQuery] string? search,
            [FromQuery] bool? aktif,          // ⭐ YENİ — sadece admin için anlamlı
            [FromQuery] bool arsiv = false,   // ⭐ YENİ (4.8) — arşivlileri göster
            [FromQuery] decimal? minFiyat = null,     // ⭐ YENİ (6.1)
            [FromQuery] decimal? maxFiyat = null,     // ⭐ YENİ (6.1)
            [FromQuery] string? kategoriler = null,   // ⭐ YENİ (6.1)
            [FromQuery] double? minPuan = null,       // ⭐ YENİ (6.1)
            [FromQuery] bool sadeceStokta = false,    // ⭐ YENİ (6.1)
            [FromQuery] string? siralama = null,      // ⭐ YENİ (6.1)
            [FromQuery] string? idler = null)         // ⭐ YENİ (GV/Faz 4)
        {
            // Rolü bir kez okuyup değişkene alıyoruz. Aşağıda iki ayrı yerde
            // lazım olacak; her seferinde token'daki claim listesini taramanın
            // anlamı yok.
            var adminMi = User.IsInRole("admin");

            var query = UrunSorgusuKur(
                adminMi, categoryId, search, aktif, arsiv,
                minFiyat, maxFiyat, kategoriler, minPuan, sadeceStokta, idler);

            query = SiralamayiUygula(query, siralama);

            var products = await UrunDtosunaCevir(query).ToListAsync();

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

            // ⭐ YENİ (4.8) — silinebilirlik SADECE admin için.
            // Müşterinin silme diye bir eylemi yok; üç sorguyu onun
            // isteğinde de çalıştırmak boşuna maliyet olurdu.
            if (adminMi)
            {
                await SilinebilirligiDoldur(products);
            }

            return Ok(products);
        }


        // ⭐ YENİ (7.1) — ÜRÜN → DTO PROJEKSİYONU, TEK YERDE
        //
        // ⚠️ NEDEN AYRI METOT? Bu projeksiyon `GetProducts` içine
        // gömülüydü. Ana sayfa ucu (`GetAnaSayfa`) ikinci tüketici
        // oldu; kopyalasaydık yarın DTO'ya yeni bir alan eklendiğinde
        // biri güncellenir, diğeri sessizce eksik veri döndürürdü —
        // ve fark ancak "ana sayfada indirim rozeti çıkmıyor" gibi bir
        // şikâyetle anlaşılırdı.
        //
        // ⚠️ IQueryable döndürüyor, List değil: sorgu henüz
        // veritabanına gitmedi. Çağıran sıralama/limit ekleyebiliyor
        // ve hepsi tek SQL'e derleniyor. (`UrunSorgusuKur` ile aynı
        // desen.)
        //
        // ⚠️ Kategori adı için ELLE BİRLEŞTİRME (alt sorgu): Product'ta
        // gezinme özelliği yok, Include kullanılamıyor. EF bunu tek
        // SQL'e çeviriyor (LEFT JOIN), N+1 doğmuyor.
        //
        // ⚠️ Kategori silinmişse alt sorgu null döner ve alan null
        // kalır — ekran etiketi hiç çizmiyor. Boş string yazsaydık
        // "adı olmayan bir kategorisi var" gibi okunurdu.
        //
        // ⚠️ `Cost` BURADA DOLDURULUYOR ama müşteriye gitmiyor:
        // çağıran taraf admin değilse null'a çekiyor. Projeksiyondan
        // hiç almasaydık admin listesi maliyeti gösteremezdi.
        private IQueryable<ProductDto> UrunDtosunaCevir(IQueryable<Product> query)
        {
            return query.Select(p => new ProductDto
            {
                Id = p.Id,
                Name = p.Name,
                Price = p.Price,
                Stock = p.Stock,
                CategoryId = p.CategoryId,
                CategoryName = _context.Categories
                    .Where(k => k.Id == p.CategoryId)
                    .Select(k => k.Name)
                    .FirstOrDefault(),
                Barcode = p.Barcode,
                Cost = p.Cost,
                IsActive = p.IsActive,
                VatRate = p.VatRate,
                EskiFiyat = p.EskiFiyat,        // B1
                ArsivlendiMi = p.ArsivlendiMi   // 4.8
            });
        }


        // ⭐ YENİ (6.1) — SIRALAMA
        //
        // ⚠️ Beyaz liste dışındaki her değer (ve null) varsayılana düşer.
        // Varsayılan "yeni": vitrinde en son eklenen ürün önde olsun.
        //
        // ⚠️ "yeni" NEDEN Id'YE GÖRE, NEDEN BİR TARİH ALANINA GÖRE DEĞİL?
        //
        // Product'ta oluşturulma tarihi YOK. Bir migration ile eklemek
        // akla geliyor ama SIRALAMA için gereksiz: Id bir identity
        // sütunu, yani artan sırada dağıtılıyor. "Hangi ürün daha yeni"
        // sorusunun cevabı Id karşılaştırmasında zaten tam doğru.
        //
        // Tarih alanı ancak "son 30 günde eklenenler" gibi bir EŞİK
        // gerektiğinde şart olur — Id ile "30 gün" diye bir şey
        // sorulamaz. O ihtiyaç Aşama 7'de (ana sayfa "Yeni gelenler"
        // bölümü) doğuyor ve migration orada alınacak. Bugün almak,
        // eski ürünlerin tarihini UYDURMAK zorunda bırakırdı: hepsine
        // bugünü yazmak "tüm katalog bugün eklendi" demek olurdu.
        //
        // ⚠️ HER SIRALAMA Id İLE BİTİYOR. Sebebi eşitlik: aynı fiyattan
        // 12 ürün varsa SQL'in sıra garantisi yoktur ve liste her
        // istekte farklı gelebilir. Sayfalama eklendiğinde bu, aynı
        // ürünün iki sayfada birden çıkmasına yol açardı. Id son
        // kırıcı olarak sırayı kararlı yapıyor.
        private IQueryable<Product> SiralamayiUygula(IQueryable<Product> query, string? siralama)
        {
            if (siralama == null || !GecerliSiralamalar.Contains(siralama))
            {
                siralama = "yeni";
            }

            switch (siralama)
            {
                case "fiyat_artan":
                    return query.OrderBy(p => p.Price).ThenBy(p => p.Id);

                case "fiyat_azalan":
                    return query.OrderByDescending(p => p.Price).ThenBy(p => p.Id);

                case "puan":
                    // ⚠️ Puansız ürün EN SONA düşüyor (?? 0), elenmiyor.
                    // minPuan filtresinden farkı bu: orada müşteri
                    // "puanı şu kadar olsun" diyor, burada sadece
                    // "iyi puanlılar önde olsun" diyor. Puansız ürünü
                    // listeden atmak, sıralama değiştirmenin sessizce
                    // ürün gizlemesi olurdu.
                    return query
                        .OrderByDescending(p => _context.Reviews
                            .Where(r => r.ProductId == p.Id && !r.IsHidden)
                            .Average(r => (double?)r.Rating) ?? 0)
                        .ThenBy(p => p.Id);

                case "populer":
                    // ⚠️ İPTAL EDİLEN SİPARİŞLER SAYILMIYOR.
                    // Sayılsaydı hiç teslim edilmemiş bir ürün "en çok
                    // satan" olabilirdi. Aynı kural ReportsController'da
                    // ciro hesabında da var; oradaki yardımcı tarih
                    // aralığına bağlı olduğu için buradan çağrılamıyor.
                    // ⭐ DEĞİŞTİ (7.1) — durum artık elle yazılmıyor.
                    // `"iptal"` metni üç dosyada elle yazılıydı ve ana
                    // sayfa bölümleri dördüncü/beşinci tüketici olunca
                    // `SiparisDurumlari` sabit sınıfına toplandı.
                    // Adı bir gün değişirse derleme hata verecek;
                    // eskiden sessizce yanlış sayardı.
                    //
                    // ⚠️ Adet toplanıyor, sipariş SAYISI değil: 1 kişinin
                    // 50 adet alması ile 50 kişinin 1'er adet alması
                    // popülerlik olarak aynı sayılmıyor derdi varsa
                    // ayrı bir ölçü gerekir; bugün "kaç adet satıldı"
                    // yeterli ve anlaşılır.
                    return query
                        .OrderByDescending(p => _context.OrderItems
                            .Where(oi => oi.ProductId == p.Id
                                      && _context.Orders.Any(o => o.Id == oi.OrderId
                                                               && o.Status != SiparisDurumlari.Iptal))
                            .Sum(oi => (int?)oi.Quantity) ?? 0)
                        .ThenBy(p => p.Id);

                default: // "yeni"
                    return query.OrderByDescending(p => p.Id);
            }
        }


        // ⭐ YENİ (6.2) — 🟢 GET /api/products/sayi?...  →  { toplam: 47 }
        //
        // Filtre panelindeki "47 ürünü göster" butonu için. Müşteri
        // kaydırıcıyı oynatırken sonucun kaç ürün olacağını UYGULAMADAN
        // görüyor.
        //
        // ⚠️ NEDEN AYRI UÇ, NEDEN LİSTE UCU { toplam, urunler } DÖNMÜYOR?
        //
        // Yol haritası 6.2'de liste cevabının `{ toplam, urunler }`
        // şeklinde sarmalanması yazıyordu. Uygulamada iki sebeple
        // ayrı uç seçildi:
        //
        //   1) Sarmalamak KIRICI bir değişiklik olurdu. Bugün
        //      GET /api/products düz dizi dönüyor ve bunu admin paneli
        //      ile mobilin dört ayrı ekranı tüketiyor. Aşama 6'nın
        //      planında admin işi YOK; cevabı sarmalamak admin panelini
        //      bu aşamanın kapsamı dışında kırardı.
        //   2) Sarmalanmış "toplam" bugün urunler.Count'a EŞİT olurdu —
        //      sayfalama yok. Yani türetilebilen bir değeri ayrıca
        //      taşımak olurdu. Sayının bağımsız bir değeri ancak listeyi
        //      İSTEMEDİĞİN durumda var, o durum da tam olarak burası.
        //
        // ⚠️ Ayrıca ucuz: panel her kaydırıcı hareketinde tam ürün
        // listesini (resimler, puanlar, silinebilirlik) çekmiyor;
        // COUNT(*) dönüyor.
        //
        // ⚠️ Rota çakışması yok: "sayi" düz metin bir segment ve
        // ASP.NET Core yönlendirmesinde düz metin, {id} gibi
        // parametreli segmentten önce gelir.
        [HttpGet("sayi")]
        public async Task<IActionResult> GetUrunSayisi(
            [FromQuery] int? categoryId,
            [FromQuery] string? search,
            [FromQuery] bool? aktif,
            [FromQuery] bool arsiv = false,
            [FromQuery] decimal? minFiyat = null,
            [FromQuery] decimal? maxFiyat = null,
            [FromQuery] string? kategoriler = null,
            [FromQuery] double? minPuan = null,
            [FromQuery] bool sadeceStokta = false,
            [FromQuery] string? idler = null)         // ⭐ YENİ (GV/Faz 4)
        {
            // ⚠️ Rol burada da okunuyor: sayaç, listenin göreceğinden
            // BAŞKA bir sayı vermemeli. Misafire "47 ürün" deyip 31
            // ürün göstermek, pasif ürünlerin varlığını sızdırırdı.
            var adminMi = User.IsInRole("admin");

            var query = UrunSorgusuKur(
                adminMi, categoryId, search, aktif, arsiv,
                minFiyat, maxFiyat, kategoriler, minPuan, sadeceStokta, idler);

            var toplam = await query.CountAsync();

            return Ok(new { toplam });
        }


        // ⭐ YENİ (6.3) — 🟢 GET /api/products/fiyat-araligi
        //                  →  { enDusuk: 89.9, enYuksek: 4499.9 }
        //
        // Mobildeki çift uçlu fiyat kaydırıcısının uçları. İstemcide sabit
        // yazsaydık (0–10.000 gibi) katalog değiştiğinde kaydırıcının yarısı
        // ölü bölge olurdu ya da en pahalı ürün aralığın dışında kalırdı.
        //
        // ⚠️ DİĞER FİLTRELERİ BİLEREK ALMIYOR — parametresiz.
        //
        // "Seçili kategoriye göre daralsın" mantıklı görünüyor ama
        // kaydırıcıyı SÜRÜKLERKEN uçların değişmesi demek olurdu:
        // parmağın altındaki tutamak yerinden kayar, seçilen değer
        // kendiliğinden başka bir sayıya döner. Sabit uç, dar uçtan iyi.
        //
        // ⚠️ Görünürlük kilidi burada da geçerli: misafire pasif ürünün
        // fiyatı üzerinden bir üst sınır vermek, o ürünün varlığını
        // sızdırırdı.
        //
        // ⚠️ Katalog boşsa Min/Max SQL'de NULL döner — (decimal?) cast'i
        // bu yüzden şart, yoksa EF "sequence contains no elements" ile
        // patlardı. Boş katalogda 0–0 dönüyoruz; kaydırıcı da o zaman
        // zaten gösterilmiyor.
        [HttpGet("fiyat-araligi")]
        public async Task<IActionResult> GetFiyatAraligi()
        {
            var adminMi = User.IsInRole("admin");

            var query = UrunSorgusuKur(
                adminMi,
                categoryId: null, search: null, aktif: null, arsiv: false,
                minFiyat: null, maxFiyat: null, kategoriler: null,
                minPuan: null, sadeceStokta: false);

            var sinirlar = await query
                .GroupBy(p => 1)
                .Select(g => new
                {
                    EnDusuk = g.Min(p => (decimal?)p.Price),
                    EnYuksek = g.Max(p => (decimal?)p.Price)
                })
                .FirstOrDefaultAsync();

            return Ok(new
            {
                enDusuk = sinirlar?.EnDusuk ?? 0m,
                enYuksek = sinirlar?.EnYuksek ?? 0m
            });
        }


        // ⭐ YENİ — 🟢 GET /api/products/5/benzer
        //
        // Aynı kategorideki diğer ürünler, popülerlik sırasında.
        [HttpGet("{id}/benzer")]
        public async Task<IActionResult> Benzer(int id, [FromQuery] int adet = 10)
        {
            if (adet < 1 || adet > 20) adet = 10;

            var kategoriId = await _context.Products
                .Where(p => p.Id == id)
                .Select(p => (int?)p.CategoryId)
                .FirstOrDefaultAsync();

            if (kategoriId == null)
            {
                return NotFound(new { mesaj = "Ürün bulunamadı!" });
            }

            var gorunur = UrunSorgusuKur(
                adminMi: false,
                categoryId: null, search: null, aktif: null, arsiv: false,
                minFiyat: null, maxFiyat: null, kategoriler: null,
                minPuan: null, sadeceStokta: false);

            var idler = await gorunur
                .Where(p => p.CategoryId == kategoriId && p.Id != id)
                .OrderByDescending(p => _context.OrderItems
                    .Where(oi => oi.ProductId == p.Id
                              && _context.Orders.Any(o => o.Id == oi.OrderId
                                                       && o.Status != SiparisDurumlari.Iptal))
                    .Sum(oi => (int?)oi.Quantity) ?? 0)
                .ThenByDescending(p => p.Id)
                .Take(adet)
                .Select(p => p.Id)
                .ToListAsync();

            return Ok(await UrunListesiGetirAsync(idler));
        }


        // ⭐ YENİ — 🟢 GET /api/products/5/birlikte-alinanlar
        //
        // Aynı siparişte geçen ürünler. ⚠️ İndirim yok, sadece öneri.
        [HttpGet("{id}/birlikte-alinanlar")]
        public async Task<IActionResult> BirlikteAlinanlar(int id, [FromQuery] int adet = 10)
        {
            if (adet < 1 || adet > 20) adet = 10;

            var idler = await _kombin.BirlikteAlinanIdlerAsync(id, adet);

            return Ok(await UrunListesiGetirAsync(idler));
        }


        // ⭐ YENİ — 🟢 GET /api/products/5/kombinler
        //
        // Admin tanımlı kombinler; indirimleri gerçek ve sepette de
        // uygulanıyor.
        [HttpGet("{id}/kombinler")]
        public async Task<IActionResult> Kombinler(int id)
        {
            return Ok(await _kombin.UrunKombinleriAsync(id));
        }


        // Verilen id'ler için tam ProductDto listesi — sırayı koruyarak.
        private async Task<List<ProductDto>> UrunListesiGetirAsync(List<int> idler)
        {
            if (idler.Count == 0)
            {
                return new List<ProductDto>();
            }

            var gorunur = UrunSorgusuKur(
                adminMi: false,
                categoryId: null, search: null, aktif: null, arsiv: false,
                minFiyat: null, maxFiyat: null, kategoriler: null,
                minPuan: null, sadeceStokta: false);

            var urunler = await UrunDtosunaCevir(gorunur.Where(p => idler.Contains(p.Id)))
                .ToListAsync();

            foreach (var u in urunler)
            {
                u.Cost = null;
            }

            StokBilgisiniDoldur(urunler, adminMi: false);
            await ResimleriDoldur(urunler);
            await PuanlariDoldur(urunler);

            return idler
                .Select(x => urunler.FirstOrDefault(u => u.Id == x))
                .Where(u => u != null)
                .Select(u => u!)
                .ToList();
        }


        // ⭐ YENİ (7.1 + 7.3 + 7.4) — 🟢 GET /api/products/anasayfa
        //
        //   ?gezilenIdler=12,5,9   (isteğe bağlı, en son bakılan başta)
        //
        // Mobil vitrinin bölümlerini TEK istekte döndürür.
        //
        // ⚠️ NEDEN TEK UÇ, NEDEN BÖLÜM BAŞINA BİR UÇ DEĞİL?
        // Beş bölüm = beş HTTP isteği demekti: mobil ağda beş ayrı
        // gidiş-dönüş, beşi ayrı zamanda dönünce ekranın parça parça
        // dolması ve bölümler arasında tutarsız bir an (biri eski,
        // biri yeni veriyle). Yol haritası 7.4'ün kuralı bu.
        //
        // ⚠️ BOŞ BÖLÜM CEVABA HİÇ GİRMİYOR. "Boş bölüm çizilmesin"
        // kuralı burada, tek yerde uygulanıyor. Her istemciye ayrı
        // `length > 0` kontrolü bıraksaydık biri unutur ve ekranda
        // başlığı olan, içi boş bir şerit kalırdı.
        //
        // ⚠️ BÖLÜM SIRASI VE BAŞLIĞI SUNUCUDAN GİDİYOR. Küratörlük
        // kararı ("hangi bölüm önce") tek yerde dursun ki yarın sıra
        // değişince uygulama güncellemesi gerekmesin.
        //
        // ⚠️ HER ZAMAN MÜŞTERİ DALI (`adminMi: false`). Ana sayfa bir
        // VİTRİN; admin bu ucu çağırsa bile pasif/arşivli ürün ya da
        // maliyet görmemeli. Rolü okumak, gizlemeyi role bağlamak
        // olurdu ve gizleme sebebi rol değil, ekranın ne olduğu.
        //
        // ⚠️ GEZME GEÇMİŞİ SUNUCUDA SAKLANMIYOR — sadece bu isteğin
        // süresince kullanılıyor (yol haritası 7.2 kararı). Misafirde
        // de çalışıyor: uç kimlik istemiyor.
        [HttpGet("anasayfa")]
        public async Task<IActionResult> GetAnaSayfa([FromQuery] string? gezilenIdler = null)
        {
            // Bozuk parçalar sessizce atılıyor, üst sınır zaten metotta.
            var gezilen = IdListesiAyristir(gezilenIdler);

            // Görünürlük kilidi TEK YERDE: bütün bölümler bu sorgunun
            // üstüne kuruluyor, yani hiçbiri pasif ya da arşivli ürün
            // gösteremez. Filtreler boş — ana sayfada filtre yok.
            var gorunur = UrunSorgusuKur(
                adminMi: false,
                categoryId: null, search: null, aktif: null, arsiv: false,
                minFiyat: null, maxFiyat: null, kategoriler: null,
                minPuan: null, sadeceStokta: false);

            // ---- 1) EN POPÜLER ÜRÜNLER — son 30 günün satışı ----
            //
            // ⚠️ PENCERE ÜÇ KEZ DEĞİŞTİ, SON HALİ 30 GÜN.
            //   7 gün  → vitrine tek ürün düşürüyordu (son 7 günde
            //            satılan 7 üründen 6'sı arşivli/pasif).
            //   sınırsız → bölüm "tüm zamanlar" oldu.
            //   30 gün → şimdiki karar.
            //
            // Otuz gün "yakın dönem" için makul bir hafıza: bir aylık
            // satış hem mevsimsel dalgayı yakalıyor hem de iki yıl
            // önce çok satmış ama artık kimsenin bakmadığı ürünü
            // vitrinde tutmuyor. Ölçüldü: bugün sınırsızla aynı 4
            // ürünü veriyor, yani kayıp yok.
            //
            // ⚠️ BAŞLIK İLE ÖLÇÜ ARASINDA BİLİNEN GERİLİM VAR.
            // "En Popüler Ürünler" adı tüm zamanlar çağrıştırıyor ama
            // ölçü 30 günlük. Bu bilinçli bir tercih: müşteri için
            // "şu sıralar çok satan" daha yararlı. Pencere bir gün
            // ekranda yazılmak istenirse başlığın altına "son 30
            // günde" alt yazısı eklenmeli — bugün eklenmedi çünkü
            // vitrinde ölçü açıklaması gürültü yapıyor.
            //
            // ⚠️ Ölçünün geri kalanı `SiralamayiUygula("populer")` ile
            // AYNI: iptal edilen siparişler sayılmıyor, adet
            // toplanıyor. Tek fark pencere; "Tümünü gör" ızgaraya
            // götürdüğünde müşteri aynı ürünlerin devamını görüyor.
            //
            // ⚠️ Görünürlük süzgeci sorgunun İÇİNDE (`gorunur.Any`):
            // EF bunu EXISTS'e çeviriyor, yani tüm ürün id'lerini
            // belleğe çekmeden filtreliyor.
            var populerEsigi = DateTime.UtcNow.AddDays(-PopulerGunSayisi);

            var populerIdler = await _context.OrderItems
                .Where(oi => gorunur.Any(p => p.Id == oi.ProductId))
                .Where(oi => _context.Orders.Any(o => o.Id == oi.OrderId
                                                   && o.Status != SiparisDurumlari.Iptal
                                                   && o.CreatedAt >= populerEsigi))
                .GroupBy(oi => oi.ProductId)
                .OrderByDescending(g => g.Sum(x => x.Quantity))
                .ThenBy(g => g.Key)     // eşitlikte kararlı sıra
                .Select(g => g.Key)
                .Take(BolumUrunSayisi)
                .ToListAsync();

            // ---- 2) EN ÇOK FAVORİLENEN ----
            //
            // ⚠️ Favori bir NİYET kaydı, satış değil: "almadım ama
            // gözüm üstünde". Satışla aynı şeyi ölçmediği için ayrı
            // bir bölüm olmayı hak ediyor.
            //
            // ⭐ DEĞİŞTİ — sayı da geliyor, sadece id değil.
            //
            // ⚠️ Şerit "en çok favorilenen" diyor ama KAÇ KİŞİ olduğunu
            // söylemiyordu; sıralamanın dayandığı ölçü görünmüyordu.
            // Sayı zaten bu sorgunun içinde hesaplanıyor (sıralama ona
            // göre); ayrıca FavorileriDoldur çağırmak aynı GROUP BY'ı
            // ikinci kez çalıştırmak olurdu.
            var favoriSayilari = await _context.Favorites
                .Where(f => gorunur.Any(p => p.Id == f.ProductId))
                .GroupBy(f => f.ProductId)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => new { ProductId = g.Key, Sayi = g.Count() })
                .Take(BolumUrunSayisi)
                .ToListAsync();

            var favoriIdler = favoriSayilari.Select(f => f.ProductId).ToList();

            // ---- 3) YENİ GELENLER ----
            //
            // ⚠️ `Id`'YE GÖRE — ve bu bir eksiklik değil, karar.
            // `Id` bir identity sütunu, artan sırada dağıtılıyor;
            // "en son eklenen 10 ürün" sorusunun cevabı Id
            // karşılaştırmasında tam doğru. Bir `CreatedAt` kolonu
            // ancak "son 30 günde eklenenler" gibi bir EŞİK
            // gerektiğinde şart olur. Bugün eklemek, mevcut 52 ürünün
            // tarihini UYDURMAK zorunda bırakırdı.
            var yeniIdler = await gorunur
                .OrderByDescending(p => p.Id)
                .Take(BolumUrunSayisi)
                .Select(p => p.Id)
                .ToListAsync();

            // ---- 4) SON GEZDİKLERİN ----
            //
            // ⚠️ SIRA PARAMETREDEN GELİYOR, VERİTABANINDAN DEĞİL.
            // "En son bakılan başta" bilgisi yalnızca cihazdaki
            // listede var; SQL onu bilemez. Görünmez olanları
            // (silinmiş, pasifleşmiş) eleyip kalanları istekteki
            // sırayla diziyoruz.
            var gorunurGezilen = await gorunur
                .Where(p => gezilen.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync();

            var sonGezilenIdler = gezilen
                .Where(id => gorunurGezilen.Contains(id))
                .Take(BolumUrunSayisi)
                .ToList();

            // ---- 5) SANA ÖZEL ----
            //
            // ⚠️ ÖNERİ MOTORU YOK VE UYDURULMUYOR. Mantık tek cümleyle
            // anlatılabilir olmalı çünkü ekranda da öyle yazıyor
            // ("son baktıklarına benzeyen"): gezilen ürünlerin
            // KATEGORİLERİNDEN, henüz bakılmamış, popüler ürünler.
            //
            // ⚠️ Gezilenlerin KENDİSİ dışarıda: müşteri onları bir
            // üstteki şeritte zaten görüyor.
            //
            // ⚠️⚠️ POPÜLER VE FAVORİ ŞERİTLERİNDEKİLER DE DIŞARIDA
            // (⭐ YENİ 2026-08-12 — cihazda fark edildi).
            //
            // Sorun: "Sana özel" popülerlik sırasına göre diziliyordu
            // ve katalog küçük olduğu için aynı çok satanlar üç şeritte
            // birden çıkıyordu. Müşterinin gördüğü, kişiselleştirmenin
            // çalışmadığıydı — üç farklı başlık, aynı ürünler.
            //
            // ⚠️ ELEME NEDEN SADECE BU BÖLÜMDE?
            // "En popüler" ve "en çok favorilenen" birer İDDİA: en çok
            // satanı listeden çıkarmak başlığı yalan yapar. "Sana özel"
            // ise bir ÖNERİ — içinden ürün çıkarmak onu yanlış değil,
            // sadece daha yararlı yapar. Bu yüzden eleme burada.
            //
            // ⚠️ "Yeni gelenler" elenmiyor: yeni ürünün çok satan olma
            // ihtimali zaten düşük ve o bölüm bunun ALTINDA çiziliyor.
            var vitrindeGosterilenler = populerIdler
                .Concat(favoriIdler)
                .Distinct()
                .ToList();

            var ilgiliKategoriler = await gorunur
                .Where(p => sonGezilenIdler.Contains(p.Id))
                .Select(p => p.CategoryId)
                .Distinct()
                .ToListAsync();

            var sanaOzelIdler = ilgiliKategoriler.Count == 0
                ? new List<int>()
                : await gorunur
                    .Where(p => ilgiliKategoriler.Contains(p.CategoryId)
                             && !gezilen.Contains(p.Id)
                             && !vitrindeGosterilenler.Contains(p.Id))
                    // Popülerlik: `SiralamayiUygula("populer")` ile
                    // aynı ölçü — iptal edilen siparişler sayılmıyor.
                    .OrderByDescending(p => _context.OrderItems
                        .Where(oi => oi.ProductId == p.Id
                                  && _context.Orders.Any(o => o.Id == oi.OrderId
                                                           && o.Status != SiparisDurumlari.Iptal))
                        .Sum(oi => (int?)oi.Quantity) ?? 0)
                    .ThenByDescending(p => p.Id)
                    .Take(BolumUrunSayisi)
                    .Select(p => p.Id)
                    .ToListAsync();

            // ---- ÜRÜNLERİ TEK SEFERDE ÇEK ----
            //
            // ⚠️ N+1'İN BÖLÜM SÜRÜMÜ. Her bölüm kendi ürünlerini ayrı
            // çekip ayrı doldursaydı resim ve puan sorguları BEŞ KEZ
            // çalışırdı — üstelik aynı ürün birden çok bölümde
            // olduğu için işin çoğu tekrar olurdu. Yukarıdaki
            // sorgular yalnızca ID topladı; ürünün kendisi, resmi ve
            // puanı bir kez geliyor.
            var tumIdler = populerIdler
                .Concat(favoriIdler)
                .Concat(yeniIdler)
                .Concat(sonGezilenIdler)
                .Concat(sanaOzelIdler)
                .Distinct()
                .ToList();

            if (tumIdler.Count == 0)
            {
                // Katalog bomboş: uydurma bir bölüm listesi değil,
                // dürüst bir boş liste. İstemci ızgarayı yine çiziyor.
                return Ok(new { bolumler = Array.Empty<object>() });
            }

            var urunler = await UrunDtosunaCevir(
                    gorunur.Where(p => tumIdler.Contains(p.Id)))
                .ToListAsync();

            // ⚠️ MALİYET MÜŞTERİYE GİTMEZ — ProductDto'daki desenin
            // aynısı. Bu uç her zaman müşteri dalında çalışıyor.
            foreach (var u in urunler)
            {
                u.Cost = null;
            }

            // Ham stok da gitmiyor; yerine `stokDurumu` + `kalanAdet`.
            StokBilgisiniDoldur(urunler, adminMi: false);

            await ResimleriDoldur(urunler);
            await PuanlariDoldur(urunler);

            var sozluk = urunler.ToDictionary(u => u.Id);

            // ⭐ YENİ — favori şeridindeki ürünlere "kaç kişi favoriledi".
            //
            // ⚠️ YALNIZCA FAVORİ ŞERİDİNDEKİLER DOLUYOR. Diğer ürünlerde
            // 0 kalıyor ve mobil bu sayıyı sadece favori şeridinde
            // çiziyor — sayının anlamı başlıktan geliyor ("en çok
            // favorilenen"), rastgele bir kartta kalp sayısı göstermek
            // müşteriye ne yapacağını bilmediği bir rakam verirdi.
            //
            // ⚠️ DTO'lar şeritler arasında PAYLAŞILIYOR (aynı ürün hem
            // popülerde hem favoride olabilir, sözlükte tek nesne).
            // Bu yüzden gösterim kararı sunucunun doldurup
            // doldurmamasına DEĞİL, mobilin şerit anahtarına bakıyor.
            foreach (var favori in favoriSayilari)
            {
                if (sozluk.TryGetValue(favori.ProductId, out var urun))
                {
                    urun.FavoriteCount = favori.Sayi;
                }
            }

            // ---- BÖLÜMLERİ KUR ----
            var bolumler = new List<object>();

            void BolumEkle(string anahtar, string baslik, List<int> idler)
            {
                var liste = idler
                    .Where(sozluk.ContainsKey)
                    .Select(id => sozluk[id])
                    .ToList();

                // Boş bölüm cevaba hiç girmiyor.
                if (liste.Count == 0)
                {
                    return;
                }

                bolumler.Add(new { anahtar, baslik, urunler = liste });
            }

            // ⚠️ SIRA BİLİNÇLİ (⭐ DEĞİŞTİ 2026-08-12): önce müşterinin
            // KENDİ izi — son gezdikleri ve ondan türeyen öneri; ikisi
            // yan yana durmalı çünkü biri diğerinin sebebi. Sonra
            // mağazanın söyledikleri: en çok satan, en çok favorilenen,
            // en yeni.
            //
            // Popüler bölümü önce en üstteydi; kişisel şeritlerin
            // altına alındı. Vitrin "herkese aynı şey" ile değil,
            // müşteriye ait olanla açılıyor. Geçmişi olmayan müşteride
            // (ilk açılış) o iki bölüm hiç gelmiyor ve sayfa zaten
            // popülerle başlıyor.
            BolumEkle("son_gezilen", "Son gezdiğin ürünler", sonGezilenIdler);
            BolumEkle("sana_ozel", "Sana özel", sanaOzelIdler);
            BolumEkle("populer", "En Popüler Ürünler", populerIdler);
            BolumEkle("favori", "En çok favorilenen", favoriIdler);
            BolumEkle("yeni", "Yeni gelenler", yeniIdler);

            return Ok(new { bolumler });
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

            // ⭐ YENİ (B4) — kategori adı. Tek kayıt, tek küçük sorgu.
            var kategoriAdi = await _context.Categories
                .Where(k => k.Id == product.CategoryId)
                .Select(k => k.Name)
                .FirstOrDefaultAsync();

            var dto = new ProductDto
            {
                Id = product.Id,
                Name = product.Name,
                Price = product.Price,
                Stock = product.Stock,
                CategoryId = product.CategoryId,
                CategoryName = kategoriAdi,
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
                VatRate = product.VatRate,
                EskiFiyat = product.EskiFiyat,   // ⭐ YENİ (B1)

                ArsivlendiMi = product.ArsivlendiMi   // ⭐ YENİ (4.8)
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

        // ⭐ DEĞİŞTİ (2026-08-12) — KURAL BU DOSYADAN ÇIKTI.
        //
        // İndirim öncesi fiyat doğrulaması artık
        // `Services/IndirimKurali.cs`'te. Sebebi: Excel içe aktarma
        // üçüncü tüketici oldu ve kural iki yerde ayrı yazılsaydı
        // panelden reddedilen bir eski fiyat Excel'den geçebilirdi.
        // Gerekçelerin tamamı o dosyada.

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
                VatRate = dto.VatRate,

                // ⭐ YENİ (B1) — indirim öncesi fiyat.
                //
                // ⚠️ BU SATIR EKSİKTİ VE SESSİZ BİR HATAYDI.
                // DTO alanı taşıyordu, UpdateProduct yazıyordu, ama
                // CreateProduct atlıyordu: panelden indirimli olarak
                // OLUŞTURULAN ürünün eski fiyatı hiçbir hata vermeden
                // kayboluyordu. Admin kaydediyor, "başarılı" mesajını
                // görüyor, indirim yok. Sonradan bir kez daha kaydetmek
                // (güncelleme yolu) düzeltiyordu — yani hata "bazen
                // çalışıyor" gibi görünürdü.
                //
                // Doğrulama UpdateProduct ile AYNI yardımcıdan geçiyor;
                // iki yolun kuralı ayrışamaz.
                EskiFiyat = IndirimKurali.EskiFiyatiDogrula(dto.EskiFiyat, dto.Price)

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

            // ⭐ YENİ (B1)
            product.EskiFiyat = IndirimKurali.EskiFiyatiDogrula(dto.EskiFiyat, dto.Price);

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




        // 🔴 PUT /api/products/5/arsiv — arşivle / arşivden çıkar
        //
        // ⚠️ NEDEN /durum'DAN AYRI BİR UÇ?
        // İkisi farklı sorulara cevap veriyor: /durum "satılsın mı?",
        // burası "listemde dursun mu?". Tek uçta birleştirseydik
        // "arşivle ama satışta kalsın" gibi anlamsız bir kombinasyon
        // çağrılabilir hale gelirdi.
        //
        // StatusToggleDto yeniden kullanılıyor — kullanıcı, kupon ve
        // ürün durumu da aynı DTO'yu kullanıyor.
        [Authorize(Roles = "admin")]
        [HttpPut("{id}/arsiv")]
        public async Task<IActionResult> ToggleArsiv(int id, [FromBody] StatusToggleDto dto)
        {
            var urun = await _context.Products.FindAsync(id);

            if (urun == null)
            {
                return NotFound(new { mesaj = "Ürün bulunamadı." });
            }

            urun.ArsivlendiMi = dto.IsActive;

            // ⚠️ ARŞİVLENEN ÜRÜN SATIŞTAN DA KALDIRILIYOR.
            //
            // "Arşivli ama hâlâ satışta" bir çelişki: ürün admin
            // listesinde görünmezken müşteri onu satın alabilirdi ve
            // gelen siparişi kimse fark etmezdi. Arşiv, satıştan
            // kaldırmanın bir üst seviyesi — alt seviyeyi de kapsar.
            //
            // ⚠️ Tersi OTOMATİK DEĞİL: arşivden çıkarmak ürünü satışa
            // AÇMIYOR. Açsaydık, aylar önce bilinçli olarak satıştan
            // kaldırılmış bir ürün arşivden çıkarılır çıkarılmaz
            // vitrine düşerdi. Satışa açmak ayrı ve bilinçli bir
            // karar olmalı.
            if (dto.IsActive)
            {
                urun.IsActive = false;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                mesaj = dto.IsActive
                    ? "Ürün arşivlendi. Satıştan da kaldırıldı."
                    : "Ürün arşivden çıkarıldı. Satışa açmak için ayrıca durumunu değiştirmelisin.",
                arsivlendiMi = urun.ArsivlendiMi,
                isActive = urun.IsActive
            });
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
        //
        // ⚠️ ARTIK HER ÜRÜN SİLİNEMİYOR (4.8).
        //
        // Bu uç eskiden koşulsuz fiziksel silme yapıyordu ve
        // veritabanında ölçülebilir hasar bıraktı: 2 silinmiş ürün,
        // 4 sahipsiz sipariş kalemi, 3 sahipsiz favori, 2 sahipsiz
        // stok hareketi. Yorumlar ise geri getirilemez şekilde gitti
        // (Reviews'ın gerçek bir FK'sı ve OnDelete(Cascade) tanımı
        // var — ürün silinince yorumlar da silindi, kaç tane olduğu
        // artık bilinemiyor).
        //
        // Hatanın bugüne kadar patlamamasının sebebi dondurma deseni:
        // OrderItem'da ad, fiyat, maliyet ve KDV oranı dondurulmuş
        // olduğu için sipariş geçmişi doğru görünmeye devam etti.
        // Yani dondurma bu hatayı MASKELEDİ, düzeltmedi.
        //
        // Kural: İŞLEM GEÇMİŞİ OLAN ÜRÜN FİZİKSEL OLARAK SİLİNMEZ.
        // Fiziksel silme yalnızca "yanlışlıkla oluşturuldu, hiç
        // kullanılmadı" durumu için.
        [Authorize(Roles = "admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            var product = await _context.Products.FindAsync(id);

            if (product == null)
            {
                return NotFound(new { mesaj = "Silinecek ürün zaten yok!" });
            }

            // ---------- GEÇMİŞ KONTROLÜ ----------
            //
            // Üç tablo da ticari/denetim kaydı: sipariş kalemleri
            // (ne satıldı), yorumlar (müşteri ne dedi), stok defteri
            // (stok neden değişti).
            //
            // ⚠️ Favorites ve StockAlerts BİLEREK sayılmıyor.
            // Onlar bir NİYET kaydı, bir işlem değil: "bu ürünü
            // beğenmiştim", "gelince haber ver". Ürün gerçekten
            // silinebilir durumdaysa bu satırların kaybolması bir
            // veri kaybı sayılmaz ve müşteriye de bir şey ifade
            // etmez. Onları da saysaydık, tek bir favorileme yüzünden
            // hiç satılmamış bir ürün silinemez hale gelirdi.
            var siparisAdedi = await _context.OrderItems
                .CountAsync(oi => oi.ProductId == id);

            var yorumAdedi = await _context.Reviews
                .CountAsync(r => r.ProductId == id);

            var hareketAdedi = await _context.StockMovements
                .CountAsync(sm => sm.ProductId == id);

            if (siparisAdedi > 0 || yorumAdedi > 0 || hareketAdedi > 0)
            {
                // ⚠️ MESAJ SAYILARI İÇERİYOR.
                // "Bu ürün silinemez" demek adminin sadece elini
                // bağlar; NEDEN silinemediğini ve bunun yerine ne
                // yapabileceğini söylemek ona bir yol gösteriyor.
                var sebepler = new List<string>();

                if (siparisAdedi > 0) sebepler.Add($"{siparisAdedi} sipariş kalemi");
                if (yorumAdedi > 0) sebepler.Add($"{yorumAdedi} yorum");
                if (hareketAdedi > 0) sebepler.Add($"{hareketAdedi} stok hareketi");

                // 409 Conflict: istek geçerli ama kaynağın MEVCUT
                // DURUMU onu imkânsız kılıyor. 400 (hatalı istek)
                // değil — istekte yanlış bir şey yok. 403 de değil —
                // adminin yetkisi var, engel yetki değil veri.
                return Conflict(new
                {
                    mesaj = $"Bu ürün {string.Join(", ", sebepler)} ile ilişkili, silinemez. "
                          + "Satıştan kaldırabilir veya arşivleyebilirsin.",
                    silinemez = true,
                    siparisAdedi,
                    yorumAdedi,
                    hareketAdedi
                });
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

            // ⭐ YENİ — ÜRÜNE İŞARET EDEN "NİYET" SATIRLARINI DA TEMİZLE
            //
            // ⚠️ Yukarıdaki engel "geçmişi olan ürün silinemez" diyor ve
            // Favorites/StockAlerts'i bilerek saymıyor (bkz. oradaki
            // gerekçe: onlar bir işlem değil, bir niyet kaydı). Ama
            // "silme engeli sayılmıyor" ile "temizlenmiyor" farklı
            // şeyler — sayılmadıkları için ürün silinebiliyor, sonra da
            // sahipsiz satır olarak kalıyorlardı.
            //
            // ⚠️ EN ZARARLISI CartItems'tı ve sessizdi:
            //   • CartController.GetCart, Products ile INNER JOIN yapıyor
            //     → satır sepet ekranından KAYBOLUYOR
            //   • OrdersController ise sepeti ham CartItems'tan okuyor
            //     → satırı GÖRÜYOR ve "Ürün bulunamadı (id: N)" deyip
            //       siparişi reddediyor
            // Yani müşteri, sepetinde göremediği bir ürün yüzünden
            // sipariş veremiyor ve sebebini öğrenmesinin hiçbir yolu yok.
            //
            // ExecuteDeleteAsync: tek SQL cümlesi, change tracker'a
            // yüklemeden siler. (Hesap kapatmadaki desenin aynısı.)
            await _context.CartItems.Where(c => c.ProductId == id).ExecuteDeleteAsync();
            await _context.Favorites.Where(f => f.ProductId == id).ExecuteDeleteAsync();
            await _context.StockAlerts.Where(s => s.ProductId == id).ExecuteDeleteAsync();

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