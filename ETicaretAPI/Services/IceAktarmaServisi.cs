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

        public IceAktarmaServisi(AppDbContext context)
        {
            _context = context;
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
                    await _context.SaveChangesAsync();

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