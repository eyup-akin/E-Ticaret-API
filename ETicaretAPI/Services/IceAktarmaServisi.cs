using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;

namespace ETicaretAPI.Services
{
    // Excel içe aktarma iş mantığı. Hangfire'dan bağımsız — Hangfire sadece
    // UrunleriIceAktar'ı tetikler. Bu servis Scoped kayıtlı, Hangfire onu
    // kendi scope'unda TAZE bir DbContext ile üretir.
    public class IceAktarmaServisi
    {
        private readonly AppDbContext _context;

        // ⭐ YENİ — dış URL'lerden resim indirmek için HttpClient fabrikası.
        //    (Program.cs'te AddHttpClient() ile zaten kayıtlı; buraya enjekte ediyoruz.)
        private readonly IHttpClientFactory _httpFactory;

        // ⭐ YENİ — wwwroot'un diskteki yerini bilmek için (resmi oraya yazacağız).
        private readonly IWebHostEnvironment _env;

        // ⭐ YENİ — resim kuralları, tek yerde dursun (ProductsController ile aynı sınırlar).
        private const long MaxResimBoyutu = 5 * 1024 * 1024; // 5 MB
        private const int MaxResimSayisi = 8;                // bir ürüne en fazla 8 resim

        public IceAktarmaServisi(
            AppDbContext context,
            IHttpClientFactory httpFactory,   // ⭐ YENİ
            IWebHostEnvironment env)          // ⭐ YENİ
        {
            _context = context;
            _httpFactory = httpFactory;
            _env = env;
        }

        // Hangfire bunu arka planda çağırır.
        public async Task UrunleriIceAktar(int jobId, string dosyaYolu)
        {
            var job = await _context.ImportJobs.FindAsync(jobId);
            if (job == null)
            {
                return;
            }

            try
            {
                job.Status = "Isleniyor";
                await _context.SaveChangesAsync();

                if (!File.Exists(dosyaYolu))
                {
                    throw new Exception("Yüklenen dosya bulunamadı.");
                }

                using var wb = new XLWorkbook(dosyaYolu);
                var ws = wb.Worksheet(1);

                // 1) Başlık satırını (1. satır) oku → isim -> sütun no sözlüğü
                var basliklar = new Dictionary<string, int>();
                foreach (var hucre in ws.Row(1).CellsUsed())
                {
                    var ad = hucre.GetString().Trim().ToLower();
                    if (!string.IsNullOrEmpty(ad) && !basliklar.ContainsKey(ad))
                    {
                        basliklar[ad] = hucre.Address.ColumnNumber;
                    }
                }

                // Bir alanın sütununu, olası isimlerinden bul
                int? SutunNo(params string[] adaylar)
                {
                    foreach (var a in adaylar)
                    {
                        if (basliklar.TryGetValue(a, out var no))
                        {
                            return no;
                        }
                    }
                    return null;
                }

                var barkodSutun = SutunNo("barkod", "barcode");
                var adSutun = SutunNo("urun adi", "ürün adı", "ad", "isim", "name");
                var fiyatSutun = SutunNo("fiyat", "price");
                var maliyetSutun = SutunNo("maliyet", "cost");
                var stokSutun = SutunNo("stok", "stock", "adet");
                var kategoriSutun = SutunNo("kategori", "category");

                // ⭐ YENİ — resim/görsel sütunu (isteğe bağlı; birden çok isim destekleniyor)
                var resimSutun = SutunNo(
                    "resim", "resimler", "gorsel", "görsel", "gorseller", "görseller",
                    "resim url", "görsel url", "gorsel url", "image", "images", "image url", "url");

                // 2) Zorunlu sütunlar var mı?
                var eksik = new List<string>();
                if (barkodSutun == null) eksik.Add("Barkod");
                if (adSutun == null) eksik.Add("Urun Adi");
                if (fiyatSutun == null) eksik.Add("Fiyat");
                if (kategoriSutun == null) eksik.Add("Kategori");

                if (eksik.Count > 0)
                {
                    throw new Exception("Excel'de şu zorunlu sütunlar yok: " + string.Join(", ", eksik));
                }

                // 3) Hızlı arama için mevcut kategori ve barkodları belleğe al
                var kategoriSozluk = (await _context.Categories.ToListAsync())
                    .ToDictionary(k => k.Name.Trim().ToLower(), k => k.Id);

                var barkodSeti = new HashSet<string>(
                    await _context.Products
                        .Where(p => p.Barcode != null)
                        .Select(p => p.Barcode!)
                        .ToListAsync(),
                    StringComparer.OrdinalIgnoreCase);

                var sonKullanilan = ws.LastRowUsed();
                if (sonKullanilan == null)
                {
                    // Boş dosya
                    job.Total = 0;
                    job.Status = "Tamamlandi";
                    job.CompletedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    try { File.Delete(dosyaYolu); } catch { }
                    return;
                }

                var sonSatir = sonKullanilan.RowNumber();
                job.Total = Math.Max(0, sonSatir - 1); // başlık hariç tahmini
                await _context.SaveChangesAsync();

                // ⭐ YENİ — Tüm satırlar için TEK bir HttpClient kur ve tekrar kullan.
                //    Her satırda yenisini açmak yerine bir tane açıp paylaşmak daha verimli.
                //    Bazı siteler "tarayıcı değilsen vermem" der → kimlik (User-Agent) ekliyoruz.
                var client = _httpFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (ETicaretAPI)");

                // 4) Satırları işle (2. satırdan itibaren)
                for (int r = 2; r <= sonSatir; r++)
                {
                    var satir = ws.Row(r);

                    // Tamamen boş satırı atla
                    if (!satir.CellsUsed().Any())
                    {
                        continue;
                    }

                    var barkod = satir.Cell(barkodSutun!.Value).GetString().Trim();
                    var ad = satir.Cell(adSutun!.Value).GetString().Trim();
                    var kategoriAdi = satir.Cell(kategoriSutun!.Value).GetString().Trim();

                    // --- Zorunlu alan kontrolü ---
                    if (string.IsNullOrEmpty(ad) ||
                        string.IsNullOrEmpty(barkod) ||
                        string.IsNullOrEmpty(kategoriAdi))
                    {
                        job.Failed++;
                        await _context.SaveChangesAsync();
                        continue;
                    }

                    // --- Barkod tekrar mı? (DB'de ya da bu dosyada) ---
                    if (barkodSeti.Contains(barkod))
                    {
                        job.Failed++;
                        await _context.SaveChangesAsync();
                        continue;
                    }

                    // --- Fiyat geçerli mi? ---
                    if (!DecimalOku(satir.Cell(fiyatSutun!.Value), out var fiyat) || fiyat <= 0)
                    {
                        job.Failed++;
                        await _context.SaveChangesAsync();
                        continue;
                    }

                    // --- Maliyet ve stok isteğe bağlı ---
                    decimal? maliyet = null;
                    if (maliyetSutun != null &&
                        DecimalOku(satir.Cell(maliyetSutun.Value), out var m))
                    {
                        maliyet = m;
                    }

                    int stok = 0;
                    if (stokSutun != null)
                    {
                        stok = IntOku(satir.Cell(stokSutun.Value));
                    }

                    // --- Kategori: yoksa oluştur ---
                    var kategoriAnahtar = kategoriAdi.ToLower();
                    int kategoriId;

                    if (kategoriSozluk.TryGetValue(kategoriAnahtar, out var mevcutId))
                    {
                        kategoriId = mevcutId;
                    }
                    else
                    {
                        var yeniKategori = new Category { Name = kategoriAdi };
                        _context.Categories.Add(yeniKategori);
                        await _context.SaveChangesAsync(); // id almak için
                        kategoriSozluk[kategoriAnahtar] = yeniKategori.Id;
                        kategoriId = yeniKategori.Id;
                    }

                    // --- Ürünü oluştur ---
                    var urun = new Product
                    {
                        Name = ad,
                        Barcode = barkod,
                        Price = fiyat,
                        Cost = maliyet,
                        Stock = stok,
                        CategoryId = kategoriId
                    };

                    _context.Products.Add(urun);
                    await _context.SaveChangesAsync(); // ⭐ artık urun.Id dolu — resimler buna bağlanacak

                    // ⭐ YENİ — RESİMLERİ İNDİR
                    //    Resim sütunu varsa ve hücre boş değilse, içindeki linkleri indirmeyi dener.
                    //    Bir hücrede birden çok resim olabilir → satır sonu / ; / | ile ayrılır.
                    //    Kural: resim inmezse ürünü BAŞARISIZ sayma, sadece o resmi atla (skip-and-count).
                    if (resimSutun != null)
                    {
                        var resimHucresi = satir.Cell(resimSutun.Value).GetString().Trim();

                        if (!string.IsNullOrEmpty(resimHucresi))
                        {
                            // Linkleri ayır, boşlukları temizle, sadece http/https ile başlayanları al,
                            // en fazla MaxResimSayisi tanesini işle.
                            var linkler = resimHucresi
                                .Split(new[] { '\n', '\r', ';', '|' },
                                       StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Where(x => x.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                .Take(MaxResimSayisi)
                                .ToList();

                            foreach (var link in linkler)
                            {
                                // Her resmi tek tek indiriyoruz (sıralı). Biri patlarsa
                                // TekResimIndirVeKaydet false döner, biz sadece diğerine geçeriz.
                                await TekResimIndirVeKaydet(urun.Id, link, client);
                            }
                        }
                    }

                    barkodSeti.Add(barkod); // aynı dosyada tekrar gelirse yakala
                    job.Success++;
                    await _context.SaveChangesAsync();
                }

                // 5) Bitiş
                job.Total = job.Success + job.Failed;
                job.Status = "Tamamlandi";
                job.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                // İşi biten dosyayı sil (yer kaplamasın)
                try { File.Delete(dosyaYolu); } catch { }
            }
            catch (Exception ex)
            {
                // Toptan hata (bozuk dosya, eksik sütun vb.) → işi Hata olarak işaretle.
                // Not: Burada hatayı yutuyoruz ki Hangfire işi baştan TEKRAR denemesin
                // (tekrar denese ürünleri ikinci kez ekleme riski olurdu).
                job.Status = "Hata";
                job.ErrorMessage = ex.Message.Length > 480
                    ? ex.Message.Substring(0, 480)
                    : ex.Message;
                job.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }

        // ---------- YARDIMCILAR ----------

        // ⭐ YENİ — wwwroot klasörünün diskteki tam yolu.
        //    (ProductsController'daki WebKok() ile birebir aynı; Hangfire scope'unda
        //     WebRootPath bazen boş gelebildiği için CurrentDirectory'ye düşüyoruz.)
        private string WebKok()
        {
            return string.IsNullOrEmpty(_env.WebRootPath)
                ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
                : _env.WebRootPath;
        }

        // ⭐ YENİ — TEK BİR URL'DEN RESMİ İNDİR, DOĞRULA, KAYDET.
        //    Başarılıysa true, herhangi bir sorunda false döner (satırı/ürünü asla patlatmaz).
        //    Mantık, ProductsController.UploadImageFromUrl ile birebir aynı — tutarlılık için.
        private async Task<bool> TekResimIndirVeKaydet(int productId, string url, HttpClient client)
        {
            // 1) URL geçerli ve http/https mi? (file://, ftp:// gibi şemaları engelle)
            if (!Uri.TryCreate(url, UriKind.Absolute, out var adres) ||
                (adres.Scheme != Uri.UriSchemeHttp && adres.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            byte[] veri;

            try
            {
                // 2) İndir — en fazla 15 saniye bekle, sonra iptal et
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var cevap = await client.GetAsync(adres, cts.Token);

                if (!cevap.IsSuccessStatusCode)
                {
                    return false; // sunucu 404/403 vb. döndü → atla
                }

                // 3) Boyut ön kontrolü — sunucu Content-Length söylediyse
                if (cevap.Content.Headers.ContentLength > MaxResimBoyutu)
                {
                    return false;
                }

                veri = await cevap.Content.ReadAsByteArrayAsync(cts.Token);
            }
            catch
            {
                // Zaman aşımı, ağ hatası, çözülemeyen adres... hepsini yut, sadece atla.
                return false;
            }

            // 4) İndirilen gerçek boyut sınırda mı? (Content-Length yalan olabilir)
            if (veri.Length == 0 || veri.Length > MaxResimBoyutu)
            {
                return false;
            }

            // 5) Byte'lara bak: gerçek resim mi + hangi uzantı? (URL'nin uzantısına GÜVENMİYORUZ)
            var uzanti = ResimUzantisiBul(veri);
            if (uzanti == null)
            {
                return false;
            }

            // 6) Klasörü hazırla, benzersiz isimle diske yaz
            var klasor = Path.Combine(WebKok(), "uploads", "urunler");
            Directory.CreateDirectory(klasor);

            var yeniAd = Guid.NewGuid().ToString("N") + uzanti;
            var tamYol = Path.Combine(klasor, yeniAd);

            await File.WriteAllBytesAsync(tamYol, veri);

            // 7) Veritabanına kaydet (tekil yüklemeyle birebir aynı mantık):
            //    o ürünün ilk resmi otomatik ANA resim olsun, sıra numarası mevcut sayı olsun.
            var mevcutSayi = await _context.ProductImages.CountAsync(r => r.ProductId == productId);

            var resim = new ProductImage
            {
                ProductId = productId,
                Url = "/uploads/urunler/" + yeniAd,
                IsMain = mevcutSayi == 0,
                SortOrder = mevcutSayi
            };

            _context.ProductImages.Add(resim);
            await _context.SaveChangesAsync();

            return true;
        }

        // ⭐ YENİ — URL'den gelen ham byte'lar gerçek resim mi + doğru uzantı ne?
        //    (ProductsController'daki ResimUzantisiBul'un aynısı — uzantıyı içerikten belirliyoruz.)
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

        // Hücreyi decimal okur. Sayı hücresi ya da "199,90"/"199.90" metni olabilir.
        private static bool DecimalOku(IXLCell hucre, out decimal sonuc)
        {
            if (hucre.TryGetValue<decimal>(out sonuc))
            {
                return true;
            }

            var metin = hucre.GetString().Trim();
            if (string.IsNullOrWhiteSpace(metin))
            {
                sonuc = 0;
                return false;
            }

            // Önce nokta ondalık (199.90), sonra virgül ondalık (199,90)
            if (decimal.TryParse(metin, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out sonuc))
            {
                return true;
            }

            if (decimal.TryParse(metin, System.Globalization.NumberStyles.Any,
                    new System.Globalization.CultureInfo("tr-TR"), out sonuc))
            {
                return true;
            }

            sonuc = 0;
            return false;
        }

        // Hücreyi tam sayı okur (okunamazsa 0)
        private static int IntOku(IXLCell hucre)
        {
            if (hucre.TryGetValue<int>(out var i))
            {
                return i;
            }
            if (hucre.TryGetValue<double>(out var d))
            {
                return (int)Math.Round(d);
            }
            if (int.TryParse(hucre.GetString().Trim(), out var p))
            {
                return p;
            }
            return 0;
        }
    }
}