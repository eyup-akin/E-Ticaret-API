using ETicaretAPI.Data;
using ETicaretAPI.DTOs;
using ETicaretAPI.Models;
using ETicaretAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ETicaretAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class OrdersController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private readonly KuponServisi _kuponServisi;     // ⭐

        public OrdersController(
            AppDbContext context,
            IConfiguration config,
            KuponServisi kuponServisi)                    // ⭐
        {
            _context = context;
            _config = config;
            _kuponServisi = kuponServisi;                 // ⭐
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }



        // ============================================================
        //  SİPARİŞ NUMARASI ÜRETİCİ
        //
        //  Format: SP-YYMMDD-NNNN   (örn. SP-260724-4821)
        //
        //  Neden Id'den türetmiyoruz?
        //    Sıralı olurdu; herkes bir önceki/sonraki siparişin
        //    numarasını tahmin edebilirdi.
        //
        //  Neden tarih var?
        //    Çakışma alanını her gün sıfırlıyor. Aynı gün içinde
        //    10.000 ihtimal var — günde binlerce sipariş gelmedikçe
        //    çakışma pratikte imkânsız.
        //
        //  Neden "önce sor, sonra yaz"?
        //    Bu kontrol KOLAYLIK içindir, garanti değildir. İki istek
        //    aynı anda aynı numarayı üretip ikisi de "boş" cevabını
        //    alabilir (yarış koşulu). Asıl garantiyi veritabanındaki
        //    unique index verir: ikincisi hata alır, transaction geri
        //    alınır. Olasılığı çok düşük olduğu için bu yeterli.
        // ============================================================
        private async Task<string> SiparisNoUretAsync()
        {
            for (int deneme = 0; deneme < 10; deneme++)
            {
                var tarih = DateTime.UtcNow.ToString("yyMMdd");
                var rastgele = Random.Shared.Next(0, 10000).ToString("D4");
                var aday = $"SP-{tarih}-{rastgele}";

                var kullanilmisMi = await _context.Orders
                    .AnyAsync(o => o.OrderNumber == aday);

                if (!kullanilmisMi)
                {
                    return aday;
                }
            }

            // 10 denemede boş numara bulunamadıysa ciddi bir sorun var
            throw new InvalidOperationException("Sipariş numarası üretilemedi.");
        }



        // 🟡 POST /api/orders — sepetten sipariş oluştur + ödeme simüle et
        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] OrderCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetUserId();

            // 1) Adres gerçekten bu kullanıcının mı?
            var adres = await _context.Addresses
                .FirstOrDefaultAsync(a => a.Id == dto.AddressId && a.UserId == userId);
            if (adres == null)
            {
                return BadRequest(new { mesaj = "Geçerli bir adres seçmelisin!" });
            }

            // 2) Kart gerçekten bu kullanıcının mı?
            var kart = await _context.Cards
                .FirstOrDefaultAsync(c => c.Id == dto.CardId && c.UserId == userId);
            if (kart == null)
            {
                return BadRequest(new { mesaj = "Geçerli bir kart seçmelisin!" });
            }

            // 2b) Alıcı adını dondurmak için kullanıcı kaydını da çekiyoruz.
            //     Token'daki isim bayat olabilir — DB'deki güncel hali doğrudur.
            var kullanici = await _context.Users.FindAsync(userId);
            if (kullanici == null)
            {
                return Unauthorized(new { mesaj = "Kullanıcı bulunamadı." });
            }

            // 3) Sepeti al
            //
            // ⭐ OrderBy(ProductId) DEADLOCK ÖNLEMİ — kozmetik değil, ZORUNLU.
            //
            // Aşağıda her ürünün stoğunu düşerken satır kilidi alıyoruz ve
            // bu kilit transaction commit olana kadar tutuluyor. Sepette
            // birden fazla ürün varsa birden fazla satır kilitleniyor.
            //
            // Sıralamasaydık şu olurdu:
            //   Müşteri A sepeti [9, 5] → önce 9'u kilitler, sonra 5'i ister
            //   Müşteri B sepeti [5, 9] → önce 5'i kilitler, sonra 9'u ister
            //   → ikisi de birbirini bekler = DEADLOCK
            //
            // Herkes ProductId'ye göre artan sırada kilitlerse döngüsel
            // bekleme oluşamaz. Biri diğerini kısa süre bekler, o kadar.
            // Buna "lock ordering" denir ve eşzamanlı programlamanın
            // temel kurallarındandır.
            var sepetOgeleri = await _context.CartItems
                .Where(ci => ci.UserId == userId)
                .OrderBy(ci => ci.ProductId)
                .ToListAsync();

            if (sepetOgeleri.Count == 0)
            {
                return BadRequest(new { mesaj = "Sepetin boş biladerim!" });
            }

            // 4) TRANSACTION başlat — ya hep ya hiç
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                decimal toplamTutar = 0;
                var siparisDetaylari = new List<OrderItem>();

                // 5) Her sepet öğesi için: ürünü bul, stoğu ATOMİK düş, fiyatı dondur
                //
                // ⚠️ ESKİ KOD YARIŞ KOŞULUNA AÇIKTI:
                //       if (urun.Stock < oge.Quantity) return ...;   // OKU + KONTROL
                //       urun.Stock -= oge.Quantity;                  // YAZ
                //
                //    Bu üç adım arasında boşluk var. İki istek aynı anda
                //    gelirse ikisi de kontrolü geçip ikisi de yazabilir
                //    (TOCTOU). Transaction bunu ÇÖZMEZ; SQL Server'ın
                //    varsayılan yalıtımı READ COMMITTED'dır ve okuma kilidi
                //    satır okunur okunmaz bırakılır.
                //
                //    Sonuç: son 1 ürün iki müşteriye birden satılabilirdi.
                foreach (var oge in sepetOgeleri)
                {
                    // Ürünün adı ve fiyatı lazım (hata mesajı + fiyat dondurma).
                    var urun = await _context.Products.FindAsync(oge.ProductId);

                    if (urun == null)
                    {
                        return BadRequest(new { mesaj = $"Ürün bulunamadı (id: {oge.ProductId})" });
                    }

                    // Lambda içinde kullanacağımız için yerel değişkene alıyoruz.
                    // Böylece EF'in ifade ağacına ne gireceği net görünüyor.
                    var adet = oge.Quantity;

                    // ⭐ ATOMİK STOK DÜŞÜRME
                    //
                    // Ürettiği SQL tek bir cümle:
                    //     UPDATE Products
                    //     SET Stock = Stock - @adet
                    //     WHERE Id = @id AND Stock >= @adet
                    //
                    // Kontrol ve yazma AYNI cümlede olduğu için araya kimse
                    // giremez: SQL Server satıra kilit koyar, koşulu
                    // değerlendirir, yazar ve kilidi commit'e kadar tutar.
                    //
                    // Ayrıca WHERE koşulu stoğun negatife düşmesini de
                    // veritabanı seviyesinde imkânsız kılıyor.

                    //
                    // ⭐ YENİ — "&& p.IsActive" KOŞULU
                    //
                    // Neden ayrı bir if değil de WHERE'in içinde?
                    //
                    // Yukarıda FindAsync ile ürünü çektik. O okuma ile bu
                    // UPDATE arasında bir zaman aralığı var. Admin tam o
                    // aralıkta ürünü satıştan kaldırırsa, ayrı bir
                    // "if (!urun.IsActive) return" kontrolü BAYAT veriye
                    // bakıyor olurdu ve sipariş yine geçerdi.
                    //
                    // Koşulu UPDATE'in WHERE'ine koyunca kontrol ile yazma
                    // aynı cümlede, aynı satır kilidi altında oluyor.
                    // Araya kimse giremiyor. Stok kontrolünde uyguladığımız
                    // desenin aynısı — TOCTOU (oku-kontrol et-yaz) yarışını
                    // kapatmak.
                    var etkilenenSatir = await _context.Products
                        .Where(p => p.Id == oge.ProductId && p.IsActive && p.Stock >= adet)
                        .ExecuteUpdateAsync(s => s.SetProperty(
                            p => p.Stock,
                            p => p.Stock - adet));

                    // UPDATE kaç satırı etkiledi?
                    //   1 → koşul tuttu, stok düşüldü
                    //   0 → koşul tutmadı, stok yetersiz
                    //
                    // Ayrı bir SELECT'e gerek yok; cevap UPDATE'in kendisinden
                    // geliyor. "Kontrol et sonra yaz" yerine "yazmayı dene,
                    // sonucuna bak" yaklaşımı.


                    if (etkilenenSatir == 0)
                    {
                        // ⭐ ARTIK İKİ SEBEP VAR: stok yetersiz VEYA ürün pasif.
                        // Etkilenen satır sayısı hangisi olduğunu söylemiyor —
                        // UPDATE sadece "koşul tutmadı" diyor. Doğru mesajı
                        // verebilmek için satırın güncel halini okuyoruz.
                        //
                        // urun.Stock ve urun.IsActive KULLANILAMAZ:
                        // ExecuteUpdateAsync change tracker'ı atladığı için
                        // bellekteki nesne bayat.
                        //
                        // İki alanı tek sorguda, anonim nesneyle alıyoruz —
                        // iki ayrı SELECT atmanın anlamı yok.
                        var durum = await _context.Products
                            .Where(p => p.Id == oge.ProductId)
                            .Select(p => new { p.Stock, p.IsActive })
                            .FirstOrDefaultAsync();

                        // return → using devreye girer → transaction rollback.
                        // Bu öğeden ÖNCEKİ ürünlerin düşülen stokları geri gelir.

                        // Önce pasifliği kontrol ediyoruz: ürün pasifse stok
                        // mesajı vermek yanıltıcı olur (stok 500 olabilir ama
                        // ürün satışta değildir).
                        if (durum != null && !durum.IsActive)
                        {
                            return BadRequest(new
                            {
                                mesaj = $"'{urun.Name}' artık satışta değil. " +
                                        "Sepetinden çıkarıp tekrar dener misin?"
                            });
                        }

                        // durum null ise ürün silinmiş demektir; kalan = 0
                        // göstermek doğru davranış (?? operatörü değil ?.
                        // kullanıyoruz çünkü durum bir nesne, Stock int).
                        return BadRequest(new
                        {
                            mesaj = $"'{urun.Name}' için yeterli stok yok! (kalan: {durum?.Stock ?? 0})"
                        });
                    }

                    // ⚠️ DİKKAT — urun.Stock'a BİLEREK DOKUNMUYORUZ.
                    //
                    // ExecuteUpdateAsync veritabanını değiştirdi ama EF'in
                    // bellekteki kopyasına haber vermedi. Burada
                    // "urun.Stock -= adet" yazsaydık entity "Modified"
                    // işaretlenir ve SaveChanges ayrıca bir UPDATE daha
                    // gönderirdi — stok iki kez düşmüş olurdu.
                    //
                    // Dokunmadığımız için entity "Unchanged" kalıyor ve
                    // SaveChanges bu satır için hiçbir SQL üretmiyor.
                    //
                    // Kural: ExecuteUpdate ile yazdığın kolona bellekte dokunma.

                    // Sipariş detayı oluştur — FİYATI DONDUR (o anki fiyat)
                    siparisDetaylari.Add(new OrderItem
                    {
                        ProductId = urun.Id,
                        Quantity = adet,
                        UnitPrice = urun.Price // o anki fiyat sabitlenir
                    });

                    toplamTutar += urun.Price * adet;
                }

                // ---------- 5b) KUPON (varsa) ----------
                //
                // ⚠️ TRANSACTION İÇİNDE olması kritik — ama neden olduğuna dikkat:
                //
                // Transaction burada ATOMİKLİK sağlıyor: kupon sayacı artışı,
                // kullanım kaydı, sipariş ve ödeme ya hep birlikte yazılır ya hiç.
                // Ortada yarım kalmış "sayaç arttı ama sipariş yok" durumu olmaz.
                //
                // ⚠️ AMA transaction yarış koşulunu ÇÖZMEZ. SQL Server'ın
                // varsayılan yalıtım seviyesi READ COMMITTED'dır; okuma kilidi
                // satır okunur okunmaz bırakılır. Yani iki istek aynı anda
                // "UsedCount < UsageLimit" kontrolünü geçebilir ve ikisi de
                // sayacı artırabilir → limit aşılır.
                //
                // Gerçek çözüm koşullu atomik UPDATE olurdu:
                //   UPDATE Coupons SET UsedCount = UsedCount + 1
                //   WHERE Id = @id AND (UsageLimit IS NULL OR UsedCount < UsageLimit)
                // ve etkilenen satır sayısını kontrol etmek.
                // Bilinen eksik olarak yol haritasına kaydedildi.
                decimal araToplam = toplamTutar;   // indirimden ÖNCEKİ tutar
                decimal indirimTutari = 0;
                string kullanilanKod = string.Empty;
                Coupon? kullanilanKupon = null;

                if (!string.IsNullOrWhiteSpace(dto.CouponCode))
                {
                    // Kupon hesabı için sepeti uygun biçime çevir
                    var kuponSepeti = new List<SepetKalemi>();

                    foreach (var oge in sepetOgeleri)
                    {
                        var u = await _context.Products.FindAsync(oge.ProductId);
                        if (u != null)
                        {
                            kuponSepeti.Add(new SepetKalemi
                            {
                                ProductId = u.Id,
                                CategoryId = u.CategoryId,
                                Adet = oge.Quantity,
                                BirimFiyat = u.Price
                            });
                        }
                    }

                    // ---- ADIM 1: DEĞER KONTROLLERİ ----
                    // Tarih, aktiflik, kategori, minimum tutar ve indirim hesabı.
                    // Bunlar sadece OKUMA yapan kontroller; yarış koşulundan
                    // etkilenmezler çünkü ortada güncellenen bir sayaç yok.
                    //
                    // ⚠️ DogrulaAsync içindeki 5. adım (toplam limit) ve
                    //    6. adım (kişi başı limit) kontrolleri BURADA
                    //    BAĞLAYICI DEĞİL — onlar "hızlı yol" kontrolleri.
                    //    Bağlayıcı kontrol aşağıda, kilit altında yapılıyor.
                    var kuponSonucu = await _kuponServisi
                        .DogrulaAsync(dto.CouponCode, userId, kuponSepeti);

                    if (!kuponSonucu.Gecerli)
                    {
                        // Kupon geçersizse siparişi HİÇ oluşturmuyoruz.
                        // Sessizce indirimsiz devam etmek yanlış olurdu —
                        // müşteri indirimli fiyat beklerken tam ödeme yapardı.
                        await transaction.RollbackAsync();

                        // kod = programın okuyacağı sabit, mesaj = insanın okuyacağı metin.
                        // Mobil taraf koda bakıp "kuponsuz devam edelim mi?" diye soracak.
                        // Metne bakarak karar verseydi, mesaj her düzeltildiğinde kod bozulurdu.
                        return BadRequest(new
                        {
                            mesaj = kuponSonucu.Mesaj,
                            kod = "KUPON_GECERSIZ"
                        });
                    }

                    indirimTutari = kuponSonucu.IndirimTutari;
                    kullanilanKupon = kuponSonucu.Kupon;
                    kullanilanKod = kuponSonucu.Kupon!.Code;

                    toplamTutar = araToplam - indirimTutari;


                    // ---- ADIM 2: TOPLAM LİMİTİ ATOMİK OLARAK TÜKET ----
                    //
                    // Ürettiği SQL:
                    //     UPDATE Coupons
                    //     SET UsedCount = UsedCount + 1
                    //     WHERE Id = @id
                    //       AND (UsageLimit IS NULL OR UsedCount < UsageLimit)
                    //
                    // Stoktaki desenin aynısı: kontrol ve yazma tek cümlede,
                    // satır kilidi altında. Araya girilemez.
                    //
                    // "UsageLimit IS NULL" kısmı limitsiz kuponlar için:
                    // koşul her zaman doğru olsun ki sayaç yine de artsın
                    // (istatistik ve raporlar için lazım).
                    //
                    // ⭐ BU SATIRIN İKİNCİ VE DAHA ÖNEMLİ GÖREVİ:
                    //    Kupon satırına exclusive kilit koyuyor ve bu kilit
                    //    COMMIT'e kadar tutuluyor. Aynı kuponla gelen ikinci
                    //    istek tam burada kuyruğa giriyor. Bir sonraki adımda
                    //    buna dayanacağız.
                    var kuponEtkilenen = await _context.Coupons
                        .Where(c => c.Id == kullanilanKupon.Id
                                 && (c.UsageLimit == null || c.UsedCount < c.UsageLimit))
                        .ExecuteUpdateAsync(s => s.SetProperty(
                            c => c.UsedCount,
                            c => c.UsedCount + 1));

                    if (kuponEtkilenen == 0)
                    {
                        // Koşul tutmadı → limit dolmuş.
                        // Rollback'i açıkça çağırıyoruz: kilitler metot
                        // bitmesini beklemeden hemen serbest kalsın.
                        // (using zaten yapardı ama bir tık daha erken.)
                        await transaction.RollbackAsync();
                        return BadRequest(new
                        {
                            mesaj = "Bu kuponun kullanım hakkı dolmuş.",
                            kod = "KUPON_GECERSIZ"
                        });
                    }


                    // ---- ADIM 3: KİŞİ BAŞI LİMİT (kilit altında) ----
                    //
                    // ⭐ BU SAYIM NEDEN GÜVENİLİR?
                    //
                    // Yukarıdaki UPDATE kupon satırını kilitledi. Aynı kuponu
                    // kullanan başka bir istek varsa o, UPDATE satırında
                    // bekliyor — buraya kadar gelemedi.
                    //
                    // Yani biz sayarken, aynı kuponla ilgili başka hiçbir
                    // işlem ilerlemiyor. Bizden önce commit etmiş istekler
                    // ise CouponUsages'a kayıtlarını çoktan yazmış durumda,
                    // dolayısıyla sayımımız onları görüyor.
                    //
                    // Kupon satırı burada bir MUTEX görevi görüyor.
                    //
                    // Genel prensip: birden fazla satırı ilgilendiren bir
                    // kuralı korumak istiyorsan, onların bağlı olduğu
                    // "ebeveyn" kaydı kilitle. Var olmayan satırları
                    // kilitleyemezsin ama ebeveynlerini kilitleyebilirsin.
                    var kisiselKullanim = await _context.CouponUsages
                        .CountAsync(cu => cu.CouponId == kullanilanKupon.Id
                                       && cu.UserId == userId);

                    if (kisiselKullanim >= kullanilanKupon.UsageLimitPerUser)
                    {
                        // Rollback, ADIM 2'deki sayaç artışını da geri alır.
                        // Yani reddedilen bir sipariş kuponun hakkını yemez.
                        await transaction.RollbackAsync();
                        return BadRequest(new
                        {
                            mesaj = "Bu kuponu daha önce kullandın.",
                            kod = "KUPON_GECERSIZ"
                        });
                    }
                }

                // 6) Sipariş üst bilgisini oluştur
                var siparis = new Order
                {
                    // ⭐ Benzersiz müşteri numarası
                    OrderNumber = await SiparisNoUretAsync(),

                    UserId = userId,
                    AddressId = dto.AddressId,
                    Total = toplamTutar,
                    Status = "hazirlaniyor",
                    PaymentStatus = "odendi",           // ödeme simüle: başarılı
                    CardLast4 = kart.Last4Digits,       // kullanılan kartı dondur

                    // ⭐ ADRESİ DONDUR
                    // Müşteri yarın adresini değiştirse bile bu sipariş
                    // nereye gideceğini bilmeye devam eder.
                    ShippingFullName = kullanici.FullName,
                    ShippingTitle = adres.Title,
                    ShippingCity = adres.City,
                    ShippingFullAddress = adres.FullAddress,
                    ShippingPhone = adres.Phone,       // ⭐

                    // ⭐ KUPON — dondurulmuş
                    SubTotal = araToplam,
                    CouponCode = kullanilanKod,
                    DiscountAmount = indirimTutari

                };

                _context.Orders.Add(siparis);
                await _context.SaveChangesAsync(); // siparis.Id burada üretilir

                // 7) Sipariş detaylarını siparişe bağla ve kaydet
                foreach (var detay in siparisDetaylari)
                {
                    detay.OrderId = siparis.Id;
                    _context.OrderItems.Add(detay);
                }

                // 7b) KUPON KULLANIM KAYDI
                if (kullanilanKupon != null)
                {
                    // ⚠️ SAYAÇ BURADA ARTIRILMIYOR — yukarıda ADIM 2'de
                    //    ExecuteUpdateAsync ile atomik olarak artırıldı.
                    //
                    //    Eskiden burada "kullanilanKupon.UsedCount++" vardı.
                    //    Kaldırdık çünkü:
                    //      1) O yazım yarış koşuluna açıktı (oku-artır-yaz)
                    //      2) ExecuteUpdateAsync change tracker'ı atlıyor;
                    //         burada da artırsaydık SaveChanges ikinci bir
                    //         UPDATE gönderir ve sayaç İKİ KEZ artardı
                    //
                    //    Kural: ExecuteUpdate ile yazdığın kolona bellekte
                    //    dokunma. (Stok düşürmede de aynı kural geçerli.)
                    //
                    //    Buradaki INSERT ise kilit altında yapılıyor ve
                    //    kişi başı limitin bir sonraki isteği doğru
                    //    saymasını sağlıyor.
                    _context.CouponUsages.Add(new CouponUsage
                    {
                        CouponId = kullanilanKupon.Id,
                        UserId = userId,
                        OrderId = siparis.Id,
                        DiscountAmount = indirimTutari,
                        UsedAt = DateTime.UtcNow
                    });
                }

                // 8) Ödeme kaydı oluştur (simülasyon)
                var odeme = new Payment
                {
                    OrderId = siparis.Id,
                    UserId = userId,
                    Amount = toplamTutar,
                    CardLast4 = kart.Last4Digits,
                    Status = "basarili",
                    PaidAt = DateTime.UtcNow
                };
                _context.Payments.Add(odeme);

                // 9) Sepeti temizle
                _context.CartItems.RemoveRange(sepetOgeleri);

                await _context.SaveChangesAsync();

                // 10) Her şey başarılı — transaction'ı onayla
                await transaction.CommitAsync();

                return Ok(new
                {
                    mesaj = "Sipariş oluşturuldu ve ödeme alındı biladerim!",
                    siparisId = siparis.Id,
                    siparisNo = siparis.OrderNumber,
                    araToplam = araToplam,           // ⭐
                    indirim = indirimTutari,         // ⭐
                    kuponKodu = kullanilanKod,       // ⭐
                    toplam = toplamTutar,
                    odemeDurumu = "odendi"
                });
            }
            catch (Exception ex)
            {
                // Bir şey patlarsa HER ŞEYİ geri al
                await transaction.RollbackAsync();
                return StatusCode(500, new { mesaj = "Sipariş oluşturulurken hata oldu, işlem geri alındı.", hata = ex.Message });
            }
        }



        // 🟡 GET /api/orders — benim siparişlerim
        [HttpGet]
        public async Task<IActionResult> GetMyOrders()
        {
            var userId = GetUserId();

            var orders = await _context.Orders
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.Id) // en yeni en üstte
                .Select(o => new OrderDto
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber,

                    ShippingFullName = o.ShippingFullName,
                    ShippingTitle = o.ShippingTitle,
                    ShippingCity = o.ShippingCity,
                    ShippingFullAddress = o.ShippingFullAddress,
                    ShippingPhone = o.ShippingPhone,          // ⭐

                    SubTotal = o.SubTotal,
                    CouponCode = o.CouponCode,
                    DiscountAmount = o.DiscountAmount,

                    Total = o.Total,

                    Status = o.Status,
                    PaymentStatus = o.PaymentStatus,
                    CardLast4 = o.CardLast4,

                    CreatedAt = o.CreatedAt,
                    CancelReason = o.CancelReason,
                    CancelledAt = o.CancelledAt,

                    Items = _context.OrderItems
                        .Where(oi => oi.OrderId == o.Id)
                        .Join(_context.Products,
                              oi => oi.ProductId,
                              p => p.Id,
                              (oi, p) => new OrderItemDto
                              {
                                  ProductId = p.Id,
                                  ProductName = p.Name,
                                  Quantity = oi.Quantity,
                                  UnitPrice = oi.UnitPrice
                              })
                        .ToList()
                })
                .ToListAsync();

            return Ok(orders);
        }

        // 🟡 GET /api/orders/5 — tek sipariş detayım
        [HttpGet("{id}")]
        public async Task<IActionResult> GetMyOrder(int id)
        {
            var userId = GetUserId();

            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

            if (order == null)
            {
                return NotFound(new { mesaj = "Sipariş bulunamadı!" });
            }

            var items = await _context.OrderItems
                .Where(oi => oi.OrderId == id)
                .Join(_context.Products,
                      oi => oi.ProductId,
                      p => p.Id,
                      (oi, p) => new OrderItemDto
                      {
                          ProductId = p.Id,
                          ProductName = p.Name,
                          Quantity = oi.Quantity,
                          UnitPrice = oi.UnitPrice
                      })
                .ToListAsync();

            var dto = new OrderDto
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,

                ShippingFullName = order.ShippingFullName,
                ShippingTitle = order.ShippingTitle,
                ShippingCity = order.ShippingCity,
                ShippingFullAddress = order.ShippingFullAddress,
                ShippingPhone = order.ShippingPhone,          // ⭐

                SubTotal = order.SubTotal,
                CouponCode = order.CouponCode,
                DiscountAmount = order.DiscountAmount,


                Total = order.Total,

                Status = order.Status,
                PaymentStatus = order.PaymentStatus,
                CardLast4 = order.CardLast4,

                CreatedAt = order.CreatedAt,
                CancelReason = order.CancelReason,
                CancelledAt = order.CancelledAt,

                Items = items
            };

            return Ok(dto);
        }



        // 🟡 PUT /api/orders/5/cancel — müşteri KENDİ siparişini iptal eder
        // Admin iptaliyle aynı bileşik işlem: stok iadesi + ödeme iadesi + sebep.
        [HttpPut("{id}/cancel")]
        public async Task<IActionResult> CancelMyOrder(int id, [FromBody] OrderCancelDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetUserId();

            // SADECE KENDİ siparişi — başkasının siparişini iptal edemez
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

            if (order == null)
            {
                return NotFound(new { mesaj = "Sipariş bulunamadı!" });
            }

            if (!IptalEdilebilirDurumlar.Contains(order.Status))
            {
                return BadRequest(new
                {
                    mesaj = "Bu sipariş artık iptal edilemez. " +
                            "Yalnızca hazırlanıyor veya kargoda olan siparişler iptal edilebilir."
                });
            }

            var kalemler = await _context.OrderItems
                .Where(oi => oi.OrderId == id)
                .ToListAsync();

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1) Stoğu geri ver
                foreach (var kalem in kalemler)
                {
                    var urun = await _context.Products.FindAsync(kalem.ProductId);
                    if (urun != null)
                    {
                        urun.Stock += kalem.Quantity;
                    }
                }

                // 2) Ödemeyi iade olarak işaretle (ciro hesabı bunu otomatik dışlar)
                var odemeler = await _context.Payments
                    .Where(p => p.OrderId == id)
                    .ToListAsync();

                foreach (var odeme in odemeler)
                {
                    odeme.Status = "iade";
                }

                // 3) Siparişi iptal et + sebebi kaydet
                order.Status = "iptal";
                order.PaymentStatus = "iade_edildi";
                order.CancelReason = dto.Reason.Trim();
                order.CancelledAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { mesaj = "Siparişin iptal edildi ve ödemen iade edildi.", durum = "iptal" });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw; // global middleware yakalar
            }
        }








        // ==========================================================
        //  ADMIN BÖLÜMÜ
        // ==========================================================

        // ⭐ DURUM MAKİNESİ
        // Bir sipariş hangi durumdan hangi duruma geçebilir?
        // Gerçek hayatta sipariş geri gitmez: teslim edilmiş bir sipariş
        // tekrar "hazırlanıyor" olamaz. Bu kuralı burada tanımlıyoruz.
        //
        // hazirlaniyor ──→ kargoda ──→ teslim_edildi  (son)
        //       └──────────────┴──────→ iptal          (son)
        private static readonly Dictionary<string, string[]> GecerliGecisler =
            new Dictionary<string, string[]>
            {
                ["hazirlaniyor"] = new[] { "kargoda" },
                ["kargoda"] = new[] { "teslim_edildi" },
                ["teslim_edildi"] = Array.Empty<string>(),  // son durum
                ["iptal"] = Array.Empty<string>()   // son durum
            };

        // İptal, yalnızca bu durumlardayken yapılabilir
        private static readonly string[] IptalEdilebilirDurumlar =
        {
            "hazirlaniyor",
            "kargoda"
        };

        // 🔴 GET /api/admin/orders?search=&status=&paymentStatus=&page=1&pageSize=10
        // Filtreleme ve sayfalama VERİTABANINDA yapılır.
        // Tarayıcıya sadece o sayfadaki satırlar iner — 50.000 sipariş olsa bile.
        [Authorize(Roles = "admin")]
        [HttpGet("/api/admin/orders")]
        public async Task<IActionResult> GetAllOrders(
            [FromQuery] string? search,
            [FromQuery] string? status,
            [FromQuery] string? paymentStatus,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            // Güvenlik: kullanıcı pageSize=999999 yazıp sunucuyu zorlamasın
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 10;
            }

            // Sipariş + müşteri birleşimi (tek sorgu, N+1 yok)
            var query = from o in _context.Orders
                        join u in _context.Users on o.UserId equals u.Id
                        select new { o, u };

            // --- FİLTRELER (hepsi SQL'e çevrilir) ---
            if (!string.IsNullOrWhiteSpace(search))
            {
                var arama = search.Trim();

                query = query.Where(x =>
                    x.u.FullName.Contains(arama) ||
                    x.u.Email.Contains(arama) ||
                    x.o.OrderNumber.Contains(arama) ||   // ⭐ "4821" veya "SP-260724" aranabilir
                    x.o.Id.ToString().Contains(arama));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(x => x.o.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(paymentStatus))
            {
                query = query.Where(x => x.o.PaymentStatus == paymentStatus);
            }

            // --- TOPLAM SAYI (sayfalamadan ÖNCE) ---
            var toplam = await query.CountAsync();

            // --- FİLTREYE UYAN TÜM SİPARİŞLERİN CİROSU ---
            // Not: sadece bu sayfanın değil, filtrenin TAMAMININ toplamı.
            var toplamTutar = await query.SumAsync(x => (decimal?)x.o.Total) ?? 0;

            // --- SAYFALAMA ---
            var siparisler = await query
                .OrderByDescending(x => x.o.CreatedAt)
                .Skip((page - 1) * pageSize)   // SQL: OFFSET
                .Take(pageSize)                // SQL: FETCH NEXT
                .Select(x => new
                {
                    id = x.o.Id,
                    siparisNo = x.o.OrderNumber,   // ⭐ ekranda gösterilecek
                    musteriAdi = x.u.FullName,
                    musteriEmail = x.u.Email,
                    tutar = x.o.Total,
                    durum = x.o.Status,
                    odemeDurumu = x.o.PaymentStatus,
                    kartSon4 = x.o.CardLast4,
                    tarih = x.o.CreatedAt,

                    // Kaç ÇEŞİT ürün (satır sayısı)
                    urunCesidi = _context.OrderItems.Count(oi => oi.OrderId == x.o.Id),

                    // Kaç ADET ürün (miktarların toplamı)
                    // Kaç ADET ürün (miktarların toplamı)
                    toplamAdet = _context.OrderItems
                        .Where(oi => oi.OrderId == x.o.Id)
                        .Sum(oi => (int?)oi.Quantity) ?? 0,

                    // ⭐ İLK 2 ÜRÜNÜN ADI — listede önizleme için.
                    //    Tümünü değil sadece 2'sini çekiyoruz; gerisi detayda.
                    //    Bu da alt sorgu olarak TEK SQL'e gömülür (N+1 yok).
                    ilkUrunler = _context.OrderItems
                        .Where(oi => oi.OrderId == x.o.Id)
                        .Join(_context.Products,
                              oi => oi.ProductId,
                              p => p.Id,
                              (oi, p) => p.Name)
                        .Take(2)
                        .ToList()
                })
                .ToListAsync();

            var toplamSayfa = (int)Math.Ceiling(toplam / (double)pageSize);

            return Ok(new
            {
                siparisler = siparisler,
                toplam = toplam,
                toplamTutar = toplamTutar,
                sayfa = page,
                sayfaBoyutu = pageSize,
                toplamSayfa = toplamSayfa
            });
        }

        // 🔴 GET /api/admin/orders/5 — sipariş detayı
        [Authorize(Roles = "admin")]
        [HttpGet("/api/admin/orders/{id}")]
        public async Task<IActionResult> GetOrderDetail(int id)
        {
            var order = await _context.Orders.FindAsync(id);

            if (order == null)
            {
                return NotFound(new { mesaj = "Sipariş bulunamadı!" });
            }

            var musteri = await _context.Users
                .Where(u => u.Id == order.UserId)
                .Select(u => new { u.Id, u.FullName, u.Email })
                .FirstOrDefaultAsync();

            // ⭐ ADRES ARTIK SİPARİŞİN İÇİNDEN OKUNUYOR
            //
            // Eskiden Addresses tablosuna gidiliyordu. Sonuç: müşteri
            // adresini düzenleyince GEÇMİŞ siparişin adresi de değişmiş
            // görünüyordu. Artık sipariş anında dondurulan hali okunuyor.
            //
            // Veritabanına gitmiyoruz — veri zaten elimizdeki "order"
            // nesnesinin içinde. Bir sorgu da tasarruf ettik.
            var adres = new
            {
                aliciAdi = order.ShippingFullName,
                title = order.ShippingTitle,
                city = order.ShippingCity,
                fullAddress = order.ShippingFullAddress,
                telefon = order.ShippingPhone        // ⭐
            };

            var kalemler = await _context.OrderItems
                .Where(oi => oi.OrderId == id)
                .Join(_context.Products,
                      oi => oi.ProductId,
                      p => p.Id,
                      (oi, p) => new
                      {
                          urunId = p.Id,
                          urunAdi = p.Name,
                          adet = oi.Quantity,
                          birimFiyat = oi.UnitPrice,
                          araToplam = oi.Quantity * oi.UnitPrice
                      })
                .ToListAsync();

            var odeme = await _context.Payments
                .Where(p => p.OrderId == id)
                .Select(p => new
                {
                    p.Id,
                    tutar = p.Amount,
                    durum = p.Status,
                    kartSon4 = p.CardLast4,
                    odemeTarihi = p.PaidAt
                })
                .FirstOrDefaultAsync();

            // Ön yüzün hangi butonları göstereceğini SUNUCU söylüyor.
            // Kuralı iki yerde tutmuyoruz — tek kaynak burası.
            var izinliGecisler = GecerliGecisler.ContainsKey(order.Status)
                ? GecerliGecisler[order.Status]
                : Array.Empty<string>();

            var iptalEdilebilir = IptalEdilebilirDurumlar.Contains(order.Status);

            return Ok(new
            {
                id = order.Id,
                siparisNo = order.OrderNumber,   // ⭐
                tarih = order.CreatedAt,
                tutar = order.Total,

                araToplam = order.SubTotal,
                kuponKodu = order.CouponCode,
                indirim = order.DiscountAmount,

                durum = order.Status,
                odemeDurumu = order.PaymentStatus,
                kartSon4 = order.CardLast4,

                iptalSebebi = order.CancelReason,
                iptalTarihi = order.CancelledAt,

                izinliGecisler = izinliGecisler,
                iptalEdilebilir = iptalEdilebilir,

                musteri = musteri,
                adres = adres,
                kalemler = kalemler,
                odeme = odeme
            });
        }



        // 🔴 GET /api/admin/orders/etiket?ids=5,7,12
        //
        // Kargo etiketi için gereken veriyi TOPLU döndürür.
        //
        // Neden tek tek /orders/{id} çağırmıyoruz?
        //   20 sipariş için 20 istek olurdu. Bir istekte hepsini alıyoruz.
        //   Ayrıca etiket detay sayfasından FARKLI veri istiyor:
        //   ödeme durumu/iptal sebebi gerekmiyor, mağaza bilgisi gerekiyor.
        [Authorize(Roles = "admin")]
        [HttpGet("/api/admin/orders/etiket")]
        public async Task<IActionResult> GetEtiketVerisi([FromQuery] string ids)
        {
            if (string.IsNullOrWhiteSpace(ids))
            {
                return BadRequest(new { mesaj = "En az bir sipariş seçmelisin." });
            }

            // "5,7,12" → [5, 7, 12]
            // Sayıya çevrilemeyen parçaları sessizce atıyoruz (TryParse).
            var idListesi = ids
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => int.TryParse(p.Trim(), out var n) ? n : 0)
                .Where(n => n > 0)
                .Distinct()
                .Take(50)          // tek seferde en fazla 50 etiket
                .ToList();

            if (idListesi.Count == 0)
            {
                return BadRequest(new { mesaj = "Geçerli sipariş numarası bulunamadı." });
            }

            // Siparişler + müşteri bilgisi (tek sorgu)
            var siparisler = await _context.Orders
                .Where(o => idListesi.Contains(o.Id))
                .Join(_context.Users,
                      o => o.UserId,
                      u => u.Id,
                      (o, u) => new { o, u })
                .Select(x => new
                {
                    id = x.o.Id,
                    siparisNo = x.o.OrderNumber,
                    tarih = x.o.CreatedAt,
                    tutar = x.o.Total,

                    // ⭐ DONDURULMUŞ adres — canlı Addresses tablosuna GİTMİYORUZ.
                    // Müşteri adresini değiştirse bile etiket doğru yere gider.
                    aliciAdi = x.o.ShippingFullName,
                    adresBaslik = x.o.ShippingTitle,
                    sehir = x.o.ShippingCity,
                    acikAdres = x.o.ShippingFullAddress,
                    telefon = x.o.ShippingPhone,      // ⭐ User'dan değil, siparişten
                    email = x.u.Email
                })
                .ToListAsync();

            // Her siparişin kalem sayısı — tek sorguda hepsi (N+1 yok)
            var kalemSayilari = await _context.OrderItems
                .Where(oi => idListesi.Contains(oi.OrderId))
                .GroupBy(oi => oi.OrderId)
                .Select(g => new
                {
                    orderId = g.Key,
                    cesit = g.Count(),
                    adet = g.Sum(oi => oi.Quantity)
                })
                .ToListAsync();

            // Bellekte eşleştir
            var sonuc = siparisler.Select(s =>
            {
                var kalem = kalemSayilari.FirstOrDefault(k => k.orderId == s.id);

                return new
                {
                    s.id,
                    s.siparisNo,
                    s.tarih,
                    s.tutar,
                    s.aliciAdi,
                    s.adresBaslik,
                    s.sehir,
                    s.acikAdres,
                    s.telefon,        // ⭐
                    s.email,
                    urunCesidi = kalem?.cesit ?? 0,
                    toplamAdet = kalem?.adet ?? 0
                };
            }).ToList();

            // Gönderici bilgisi config'ten — koda gömülmüyor
            var magaza = new
            {
                ad = _config["Magaza:Ad"] ?? "",
                telefon = _config["Magaza:Telefon"] ?? "",
                adres = _config["Magaza:Adres"] ?? ""
            };

            return Ok(new { magaza = magaza, etiketler = sonuc });
        }





        // 🔴 PUT /api/admin/orders/5/status — kargo durumunu İLERLET
        [Authorize(Roles = "admin")]
        [HttpPut("/api/admin/orders/{id}/status")]
        public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] StatusUpdateDto dto)
        {
            var yeniDurum = dto.Status.Trim().ToLowerInvariant();

            var order = await _context.Orders.FindAsync(id);

            if (order == null)
            {
                return NotFound(new { mesaj = "Sipariş bulunamadı!" });
            }

            // ⭐ GEÇİŞ KONTROLÜ — whitelist'in gelişmiş hâli.
            // Sadece "geçerli durum mu" değil, "BU durumdan ORAYA geçilebilir mi" diye soruyoruz.
            var izinliler = GecerliGecisler.ContainsKey(order.Status)
                ? GecerliGecisler[order.Status]
                : Array.Empty<string>();

            if (!izinliler.Contains(yeniDurum))
            {
                if (izinliler.Length == 0)
                {
                    return BadRequest(new
                    {
                        mesaj = $"Bu sipariş '{order.Status}' durumunda ve artık değiştirilemez."
                    });
                }

                return BadRequest(new
                {
                    mesaj = $"'{order.Status}' durumundan '{yeniDurum}' durumuna geçilemez. " +
                            $"İzin verilen: {string.Join(", ", izinliler)}"
                });
            }

            order.Status = yeniDurum;
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Sipariş durumu güncellendi biladerim!", durum = yeniDurum });
        }

        // 🔴 PUT /api/admin/orders/5/cancel — siparişi iptal et (sebep zorunlu)
        // Ayrı bir endpoint, çünkü iptal sadece bir "durum değişikliği" değil:
        // stok iadesi + ödeme iadesi + sebep kaydı içeren BİLEŞİK bir işlem.
        [Authorize(Roles = "admin")]
        [HttpPut("/api/admin/orders/{id}/cancel")]
        public async Task<IActionResult> CancelOrder(int id, [FromBody] OrderCancelDto dto)
        {
            var order = await _context.Orders.FindAsync(id);

            if (order == null)
            {
                return NotFound(new { mesaj = "Sipariş bulunamadı!" });
            }

            if (!IptalEdilebilirDurumlar.Contains(order.Status))
            {
                return BadRequest(new
                {
                    mesaj = $"'{order.Status}' durumundaki bir sipariş iptal edilemez. " +
                            "Yalnızca hazırlanıyor veya kargoda olan siparişler iptal edilebilir."
                });
            }

            var kalemler = await _context.OrderItems
                .Where(oi => oi.OrderId == id)
                .ToListAsync();

            // TRANSACTION: stok iadesi + ödeme iadesi + durum değişikliği
            // ya hep birlikte olur, ya hiç olmaz.
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1) STOĞU GERİ VER
                // Sipariş verilirken stok düşülmüştü; iptal edilince o ürünler
                // tekrar satılabilir olmalı.
                foreach (var kalem in kalemler)
                {
                    var urun = await _context.Products.FindAsync(kalem.ProductId);

                    if (urun != null)
                    {
                        urun.Stock += kalem.Quantity;
                    }
                }

                // 2) ÖDEMEYİ İADE OLARAK İŞARETLE
                // Böylece toplam gelir hesabı (Status == "basarili" toplamı)
                // bu tutarı OTOMATİK olarak dışarıda bırakır. Ekstra kod gerekmez.
                var odemeler = await _context.Payments
                    .Where(p => p.OrderId == id)
                    .ToListAsync();

                foreach (var odeme in odemeler)
                {
                    odeme.Status = "iade";
                }

                // 3) SİPARİŞİ İPTAL ET + SEBEBİ KAYDET
                order.Status = "iptal";
                order.PaymentStatus = "iade_edildi";
                order.CancelReason = dto.Reason.Trim();
                order.CancelledAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new
                {
                    mesaj = "Sipariş iptal edildi, stok iade edildi ve ödeme geri alındı.",
                    durum = "iptal"
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw; // global middleware yakalasın
            }
        }



    }
}