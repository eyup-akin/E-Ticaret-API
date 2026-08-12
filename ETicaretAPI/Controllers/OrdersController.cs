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

        // ⭐ YENİ — e-posta bildirimleri için üç bağımlılık
        //
        // _email     : gönderme sözleşmesi. Arkada konsol mu SMTP mi
        //              olduğunu BİLMİYORUZ ve bilmemeliyiz.
        // _sablonlar : içerik üretici.
        // _log       : GuvenliGonderAsync hatayı buraya yazacak.
        //
        // Neden logger'ı uzantı metoduna PARAMETRE olarak veriyoruz?
        // Uzantı metotları static'tir, bağımlılık enjeksiyonu alamazlar.
        // Çağıranın kendi logger'ını vermesi ayrıca faydalı: log kaydında
        // "hangi controller" bilgisi otomatik görünüyor.
        private readonly IEmailGonderici _email;
        private readonly EmailSablonlari _sablonlar;
        private readonly ILogger<OrdersController> _log;
        // ⭐ YENİ — stok hareket defteri
        private readonly StokDefteri _defter;

        // ⭐ YENİ — mağaza ayarları (sipariş no öneki için)
        private readonly MagazaAyarlari _ayarlar;

        // ⭐ YENİ — kargo/toplam hesabı.
        //
        // Toplamı burada elle hesaplamıyoruz. Aynı hesabı mobil
        // sepet ekranı ve kupon önizleme ucu da yapıyor; üçünün
        // aynı sonucu vermesinin tek garantisi aynı kodu
        // çağırmaları.
        private readonly SepetHesaplayici _hesaplayici;

        // ⭐ YENİ — KDV ayrıştırma.
        //
        // ⚠️ Bu servis TOPLAMI DEĞİŞTİRMEZ. Fiyatlar KDV dahil olduğu
        // için ödenecek tutar aynı kalıyor; burada üretilen sadece o
        // tutarın dökümü. Toplamın tek kaynağı hâlâ _hesaplayici.
        private readonly KdvHesaplayici _kdv;

        public OrdersController(
            AppDbContext context,
            IConfiguration config,
            KuponServisi kuponServisi,                    // ⭐
            IEmailGonderici email,                        // ⭐ YENİ
            EmailSablonlari sablonlar,                    // ⭐ YENİ
            StokDefteri defter,
            MagazaAyarlari ayarlar,                       // ⭐ YENİ (4.1)
            SepetHesaplayici hesaplayici,                 // ⭐ YENİ (4.2)
            KdvHesaplayici kdv,                           // ⭐ YENİ (4.3)
            ILogger<OrdersController> log)                // ⭐ YENİ
        {
            _context = context;
            _config = config;
            _kuponServisi = kuponServisi;                 // ⭐
            _email = email;
            _sablonlar = sablonlar;
            _log = log;
            _defter = defter;
            _ayarlar = ayarlar;                           // ⭐ YENİ
            _hesaplayici = hesaplayici;                   // ⭐ YENİ
            _kdv = kdv;                                   // ⭐ YENİ
        }


        // ⭐ YENİ — SİPARİŞİN KDV DÖKÜMÜNÜ ÜRETİR
        //
        // Neden ayrı bir private metot?
        // Aynı döküm İKİ uçta gösteriliyor: müşterinin sipariş detayı
        // (GetMyOrder) ve adminin sipariş detayı (GetOrderDetail).
        // İki yere kopyalasaydık, yarın kargo KDV'sinin dahil edilme
        // kuralı değiştiğinde birini güncelleyip diğerini unutmak işten
        // değildi — SiparisCevabi metodunda tam olarak bunu yaşamıştık.
        //
        // ⚠️ KALEM TUTARI = UnitPrice × Quantity, KDV DAHİL.
        // Ayrıştırma bu tutarın İÇİNDEN yapılıyor, üstüne eklenmiyor.
        //
        // ⚠️ KARGO DA DÖKÜME GİRİYOR (karar K1): kargo bir hizmettir ve
        // KDV'ye tabidir. Oranı kalemlerden bağımsız — sepette %1 gıda
        // ile %20 elektronik birlikte olabilir, kargonun kendi oranı var.
        private KdvOzeti SiparisKdvDokumu(Order order, List<OrderItem> kalemler)
        {
            var parcalar = kalemler
                .Select(k => (Tutar: k.UnitPrice * k.Quantity, Oran: k.VatRate))
                .ToList();

            // Kargo ücretsizse ShippingVatRate zaten null yazılmış
            // olur ve Ozetle onu atlar. Yine de sıfır tutarlı bir kalem
            // eklememek için burada da kontrol ediyoruz — 0 TL'lik bir
            // satırın döküme girmesi kimseye bir şey anlatmaz.
            if (order.ShippingCost > 0)
            {
                parcalar.Add((order.ShippingCost, order.ShippingVatRate));
            }

            return _kdv.Ozetle(parcalar);
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // ⭐ YENİ — bildirim için müşterinin e-posta adresi.
        //
        // Sadece tek kolon çekiyoruz (Select ile). Tüm User satırını
        // çekmenin anlamı yok — ad, şifre hash'i, güvenlik damgası
        // hepsi ağdan boşuna geçerdi.
        //
        // ?? string.Empty : kullanıcı silinmişse boş dönüyor.
        // GuvenliGonderAsync boş adresi zaten atlıyor, ek kontrol
        // gerekmiyor.
        private async Task<string> MusteriEmailiGetirAsync(int userId)
        {
            return await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync() ?? string.Empty;
        }

        // Sipariş maili için ürün satırları.
        //
        // Neden OrderItem listesini doğrudan şablona vermiyoruz?
        // Şablonun tek işi metin üretmek — veri hazırlamak çağıranın
        // görevi. Şablona entity verirsek şablon veritabanı şemasına
        // bağımlı hale gelir ve model değişince şablon da bozulur.
        //
        // ⭐ DEĞİŞTİ — ürün adı artık Products tablosundan CANLI
        // okunmuyor, kalemin İÇİNDEKİ donmuş addan alınıyor.
        //
        // Eski hali Join(_context.Products, ...) yapıyordu ve iki
        // sorunu vardı:
        //   1) Müşteri mailde, sipariş verdiği ürünün adını değil
        //      BUGÜNKÜ adını görüyordu
        //   2) Ürün silinmişse INNER JOIN o satırı düşürüyor ve mailde
        //      kalem hiç görünmüyordu — toplam tutar ile kalemler
        //      birbirini tutmuyordu
        //
        // Artık JOIN yok: tek tablodan okuyoruz, hem daha doğru hem
        // daha ucuz.
        private async Task<List<EmailSiparisKalemi>> EmailKalemleriGetirAsync(int orderId)
        {
            return await _context.OrderItems
                .Where(oi => oi.OrderId == orderId)
                .Select(oi => new EmailSiparisKalemi(
                    oi.ProductName,   // ⭐ donmuş ad
                    oi.Quantity,
                    oi.UnitPrice))
                .ToListAsync();
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
                // ⭐ DEĞİŞTİ — önek artık ayardan.
                //
                // ⚠️ Bu değeri sonradan değiştirirsen eski siparişler
                // eski önekle KALIR — ve bu doğru davranış. Sipariş
                // numarası dondurulmuş veridir; geçmişe dönüp
                // değiştirmek, müşterinin elindeki fişle sistemdeki
                // kaydı uyuşmaz hale getirirdi.
                var aday = $"{_ayarlar.SiparisNoOneki}-{tarih}-{rastgele}";

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


        // ⭐ YENİ — SİPARİŞ BAŞARI CEVABI
        //
        // Neden ayrı bir metot?
        // Bu cevabı İKİ yerde döndürüyoruz: normal akışın sonunda ve
        // "bu sipariş zaten vardı" durumunda. İki yere kopyalasaydık,
        // yarın cevaba yeni bir alan eklendiğinde (örneğin kargo
        // ücreti) birini güncelleyip diğerini unutmak işten değildi —
        // ve bunu hiçbir hata mesajı söylemezdi.
        //
        // static: sınıfın hiçbir alanına dokunmuyor, saf bir dönüşüm.
        private static object SiparisCevabi(Order o, string mesaj)
        {
            return new
            {
                mesaj = mesaj,
                siparisId = o.Id,
                siparisNo = o.OrderNumber,
                araToplam = o.SubTotal,
                indirim = o.DiscountAmount,

                // ⭐ YENİ — kargo ücreti.
                //
                // Bu metodun yorumunda tam olarak bunu öngörmüştük:
                // "yarın cevaba yeni bir alan eklendiğinde (örneğin
                // kargo ücreti) birini güncelleyip diğerini unutmak
                // işten değildi". Metot ortak olduğu için tek satırla
                // hem normal akış hem "zaten oluşturulmuştu" cevabı
                // düzeldi.
                kargoUcreti = o.ShippingCost,

                kuponKodu = o.CouponCode,
                toplam = o.Total,
                odemeDurumu = o.PaymentStatus
            };
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

            // ⭐ YENİ — ÇİFT SİPARİŞ ÖN KONTROLÜ
            //
            // "Değer yok" durumunu TEK bir şekilde temsil ediyoruz:
            // NULL. Boş string gelirse de null'a çeviriyoruz, yoksa
            // veritabanında "" değeri unique index'e girer ve ikinci
            // boş istek çakışır.
            var anahtar = string.IsNullOrWhiteSpace(dto.IdempotencyKey)
                ? null
                : dto.IdempotencyKey.Trim();

            if (anahtar != null)
            {
                // ⚠️ SAHİPLİK KONTROLÜ SORGUYA DAHİL.
                // Ayrı bir if olarak yazsaydık unutulabilirdi; burada
                // unutmak imkânsız. Başkasının anahtarıyla istek atan
                // biri asla o siparişi göremez.
                //
                // AsNoTracking: sadece okuyup döndüreceğiz, EF'in bu
                // nesneyi takip etmesine gerek yok.
                var mevcutSiparis = await _context.Orders
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.IdempotencyKey == anahtar
                                           && o.UserId == userId);

                if (mevcutSiparis != null)
                {
                    // ⚠️ 409 DEĞİL 200.
                    // İdempotentliğin tanımı: aynı istek, aynı cevap.
                    // Müşterinin siparişi VAR — ona hata göstermek
                    // yanlış olurdu. Ekran normal akışına devam etsin.
                    return Ok(SiparisCevabi(
                        mevcutSiparis,
                        "Bu sipariş zaten oluşturulmuştu."));
                }

                // ⚠️ Bu kontrol ucuz durumu ucuza halleder ama GARANTİ
                //    DEĞİLDİR: iki istek aynı anda buraya girip ikisi
                //    de "yok" cevabı alabilir. Garantiyi aşağıdaki
                //    unique index + DbUpdateException yakalaması verir.
            }

            // 1) Adres gerçekten bu kullanıcının mı?
            var adres = await _context.Addresses
                .FirstOrDefaultAsync(a => a.Id == dto.AddressId && a.UserId == userId);
            if (adres == null)
            {
                return BadRequest(new { mesaj = "Geçerli bir adres seçmelisin!" });
            }

            // ⭐ YENİ (4.9) — 1b) Adresin telefonu.
            //
            // ⚠️ AYRI SORGU, ÇÜNKÜ ADRESTE ARTIK NUMARA YOK — sadece
            // ona işaret eden bir id var (bkz. Address.PhoneId).
            //
            // ⚠️ TELEFONSUZ ADRESLE SİPARİŞ ALINMIYOR. Müşteri
            // numarayı adres oluşturduktan SONRA silmiş olabilir
            // (FK'da SET NULL) — o durumda kargo etiketi telefonsuz
            // basılır ve kurye adresi bulamazsa kimseyi arayamaz.
            // Boş bir alanla devam etmektense burada durup seçim
            // istemek doğru: "yanlış/eksik veriyle devam etme".
            var adresTelefonu = adres.PhoneId == null
                ? null
                : await _context.Phones
                    .Where(p => p.Id == adres.PhoneId && p.UserId == userId)
                    .Select(p => p.Numara)
                    .FirstOrDefaultAsync();

            if (adresTelefonu == null)
            {
                return BadRequest(new
                {
                    mesaj = "Bu adrese bağlı telefon numarası yok. Adresi düzenleyip " +
                            "bir numara seçmelisin."
                });
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

                // ⭐ YENİ — stok hareketleri BURADA BİRİKTİRİLİYOR,
                // context'e henüz eklenmiyor.
                //
                // Sebep: bu hareketlerin ReferansId'si siparişin Id'si
                // olacak ve o Id henüz yok. Context'e erken eklersek,
                // aşağıdaki ilk SaveChangesAsync onları da yazar ve
                // ReferansId kalıcı olarak NULL kalır.
                //
                // siparisDetaylari listesi de tam olarak aynı sebeple
                // var — OrderItem'ların da OrderId'si sonradan doluyor.
                var stokHareketleri = new List<StockMovement>();

                // 5) Her sepet öğesi için: ürünü bul, stoğu ATOMİK düş, fiyatı dondur
                //
                // ⚠️ ESKİ KOD YARIŞ KOŞULUNA AÇIKTI:
                //        if (urun.Stock < oge.Quantity) return ...;   // OKU + KONTROL
                //        urun.Stock -= oge.Quantity;                  // YAZ
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

                    // ⭐ YENİ — STOK HAREKETİ (satış)
                    //
                    // ⚠️ ÖNCEKİ STOĞU NEREDEN BİLİYORUZ?
                    // urun.Stock, FindAsync ile okunduğu andaki değer.
                    // ExecuteUpdateAsync veritabanını değiştirdi ama
                    // bellekteki nesneye haber vermedi — yani urun.Stock
                    // hâlâ DÜŞÜŞTEN ÖNCEKİ değeri taşıyor. Bize tam
                    // olarak o lazım.
                    //
                    // Bu, normalde "bayat veri" diye kaçındığımız durumun
                    // işimize yaradığı nadir bir yer. Düşüşün gerçekten
                    // olduğunu yukarıda etkilenenSatir == 1 ile
                    // doğruladık.
                    //
                    // ⚠️ Ekle() DEĞİL Olustur() ÇAĞIRIYORUZ.
                    // Ekle() nesneyi context'e koyardı ve aşağıdaki ilk
                    // SaveChangesAsync onu ReferansId=NULL olarak diske
                    // yazardı. Listede bekletip, sipariş Id'si oluştuktan
                    // sonra topluca ekliyoruz.
                    stokHareketleri.Add(_defter.Olustur(
                        urunId: urun.Id,
                        miktar: -adet,              // satış = eksi
                        oncekiStok: urun.Stock,
                        sebep: StokSebep.Satis,
                        kullaniciId: userId,
                        referansTipi: "Order",

                        // referansId'yi aşağıda, sipariş kaydedildikten
                        // sonra dolduracağız.
                        referansId: null));

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

                    // Sipariş detayı oluştur — ÜÇ ALANI BİRDEN DONDUR
                    //
                    // ⭐ Buradaki "urun" nesnesi yukarıda FindAsync ile
                    // çekildi. O sırada okunan değerler kalemin içine
                    // KOPYALANIYOR; bu noktadan sonra Products tablosunda
                    // ne olursa olsun bu satır değişmez.
                    //
                    // Neden üçü birlikte? Yarı donmuş kayıt en kötüsüdür:
                    // fiyat 3 ay önceki, ad bugünkü olursa hangi bilginin
                    // güvenilir olduğu belli olmaz.
                    siparisDetaylari.Add(new OrderItem
                    {
                        ProductId = urun.Id,
                        Quantity = adet,

                        // Satış fiyatı — müşterinin ödediği tutar
                        UnitPrice = urun.Price,

                        // ⭐ YENİ (B1) — o anki indirim öncesi fiyat.
                        //
                        // ⚠️ Product.EskiFiyat CANLI VERİ: kampanya
                        // bitince admin onu siliyor. Dondurmasaydık üç
                        // ay önce indirimli alınan bir siparişin
                        // "kazandın" satırı bir gün sessizce
                        // kaybolurdu.
                        //
                        // ⚠️ Ürün indirimsizse null kalıyor ve o doğru:
                        // urun.Price yazsaydık her siparişte "indirim
                        // yoktu" diye ayrıca bir iddia üretirdik ve
                        // ekranda 0 TL'lik bir kazanç satırı çıkardı.
                        EskiFiyat = urun.EskiFiyat,

                        // ⭐ YENİ — ürün adı.
                        // Ürün sonradan silinse veya adı değişse bile
                        // müşteri neyi sipariş ettiğini görebilsin.
                        ProductName = urun.Name,

                        // ⭐ YENİ — o anki maliyet.
                        // urun.Cost nullable (decimal?), UnitCost de
                        // nullable — maliyeti girilmemiş üründe null
                        // yazılır, dönüşüm gerekmez.
                        //
                        // Bu satır kâr raporunun temelidir: rapor
                        // Products tablosuna HİÇ bakmayacak, sadece
                        // buradaki donmuş değeri kullanacak.
                        UnitCost = urun.Cost,

                        // ⭐ YENİ — o anki KDV oranı.
                        //
                        // KDV oranları YASAYLA değişir ve geçmişe dönük
                        // uygulanmaz. Oranı Product'tan canlı okusaydık,
                        // oran %20'den %10'a indiği gün geçmiş faturaların
                        // hepsi yeni oranı gösterirdi — müşterinin elindeki
                        // fiş ile sistemdeki kayıt tutmazdı.
                        //
                        // UnitPrice, ProductName ve UnitCost ile aynı
                        // muamele: dondurulmuş kayıt yarı canlı olmamalı.
                        VatRate = urun.VatRate
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
                                BirimFiyat = u.Price,

                                // ⭐ YENİ (B1) — kupon "indirimli üründe
                                // geçmez" ise bu kalem matrahtan düşer.
                                //
                                // ⚠️ Koşul CouponsController'daki ile
                                // BİREBİR aynı olmak zorunda: sepette
                                // önizlenen indirim ile siparişte
                                // uygulanan indirim farklı çıkarsa
                                // müşteri gördüğü tutarı ödemez.
                                IndirimliMi = u.EskiFiyat != null && u.EskiFiyat > u.Price
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

                    // ⛔ KALDIRILDI — toplam artık aşağıda 5c'de
                    // SepetHesaplayici tarafından hesaplanıyor.
                    //
                    // Burada bıraksaydık kargo eklenmemiş bir toplam
                    // yazılırdı. Sonra 5c bunu düzeltirdi, ama kuponsuz
                    // siparişlerde bu satır hiç çalışmadığı için
                    // davranış farkı gizli kalırdı.
                    // toplamTutar = araToplam - indirimTutari;


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

                // ---------- 5c) KARGO VE NİHAİ TOPLAM ----------
                //
                // ⚠️ BU BLOK if'İN DIŞINDA — dikkat et.
                //
                // Yukarıdaki kupon bloğu "if (dto.CouponCode dolu ise)"
                // içindeydi. Bu blok onun DIŞINDA, çünkü kargo ücreti
                // kupon olsun olmasın HER siparişte hesaplanmalı.
                //
                // Kupon yoksa indirimTutari = 0 kalır (yukarıda öyle
                // ilklendi) ve hesap yine doğru çalışır.
                //
                // ⚠️ Toplamı burada ELLE hesaplamıyoruz. Aynı hesabı
                // mobil sepet ekranı ve /coupons/dogrula ucu da yapıyor.
                // Üçünün aynı sonucu vermesinin tek garantisi aynı kodu
                // çağırmaları — KuponServisi'ni de bu sebeple yazmıştık.
                //
                // Servis:
                //   • kargo eşiğini İNDİRİMLİ tutara göre değerlendirir
                //   • kargoyu indirimden SONRA ekler (kupon kargoya inmez)
                //   • tüm değerleri kuruşa yuvarlar
                var ozet = _hesaplayici.Hesapla(araToplam, indirimTutari);

                // Order.Total ve Payment.Amount'ın İKİSİ de bu değerden
                // besleniyor. Ayrı ayrı hesaplasaydık kuruş farkı çıkabilir
                // ve "sipariş 349,90 ama ödeme 349,89" gibi bir tutarsızlık
                // doğardı.
                toplamTutar = ozet.Toplam;

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
                    // ⭐ DEĞİŞTİ (4.9) — numara canlı tablodan okundu ama
                    // buraya KOPYA olarak yazılıyor; FK verilmedi.
                    //
                    // ⚠️ GÖSTERİM BİÇİMİ DONDURULUYOR ("0552 808 31 29"),
                    // kanonik hali değil. Bu alanın tek işi okunmak:
                    // kargo etiketi, sipariş detayı ve bilgilendirme
                    // maili onu olduğu gibi basıyor. Kanonik hali
                    // dondursaydık üç tüketicinin de biçimlendirmeyi
                    // hatırlaması gerekirdi ve biri unuturdu.
                    ShippingPhone = TelefonBicimi.Goster(adresTelefonu),

                    // ⭐ KUPON — dondurulmuş
                    SubTotal = araToplam,
                    CouponCode = kullanilanKod,
                    DiscountAmount = indirimTutari,

                    // ⭐ YENİ — KARGO ÜCRETİ (dondurulmuş)
                    //
                    // ⚠️ _ayarlar.KargoUcreti DEĞİL, ozet.KargoUcreti.
                    //
                    // Ayar 49,90 olsa bile müşteri eşiği geçtiyse özet
                    // 0 döndürür. Doğrudan ayardan alsaydık, ücretsiz
                    // kargo kazanan müşteriden de ücret almış gibi
                    // kayda geçerdik — Total 600 ama ShippingCost 49,90
                    // yazardı ve ikisi birbirini tutmazdı.
                    ShippingCost = ozet.KargoUcreti,

                    // ⭐ YENİ — KARGO KDV ORANI (dondurulmuş)
                    //
                    // ⚠️ KARGO ÜCRETSİZSE ORAN DA YAZILMIYOR.
                    //
                    // 0 TL'lik bir kargonun KDV oranı diye bir şey yok.
                    // %20 yazsaydık ekranda "Kargo KDV (%20): 0,00 TL"
                    // gibi anlamsız bir satır çıkardı — matematiksel
                    // olarak doğru ama bilgi taşımayan bir satır.
                    //
                    // null bırakınca KdvHesaplayici o kalemi döküme hiç
                    // katmıyor ve satır çizilmiyor.
                    ShippingVatRate = ozet.KargoUcreti > 0
                        ? _ayarlar.KargoKdvOrani
                        : (int?)null,

                    // ⭐ YENİ — MÜŞTERİ NOTU
                    //
                    // Boşsa null yazıyoruz, boş string değil.
                    //
                    // Neden fark eder? Veritabanında "" ve NULL farklı
                    // şeylerdir ve sorgularda ayrı davranırlar:
                    //   WHERE CustomerNote IS NOT NULL
                    // sorgusu boş string'i "not var" sayardı. İleride
                    // "notu olan siparişler" filtresi yazarsak boş
                    // notlar listeye dolardı.
                    //
                    // Kural: "değer yok" durumunu TEK bir şekilde temsil et.
                    // Bu projede o temsil NULL.
                    //
                    // Trim: baştaki/sondaki boşluklar mobil klavyeden çok
                    // sık geliyor ve kargo etiketinde hizalamayı bozuyor.
                    CustomerNote = string.IsNullOrWhiteSpace(dto.CustomerNote)
                        ? null
                        : dto.CustomerNote.Trim(),

                    // ⭐ YENİ — çift sipariş koruması anahtarı.
                    // Anahtar gelmediyse null yazılır; o siparişte
                    // koruma yoktur ama unique index'in filtresi
                    // sayesinde başka bir soruna da yol açmaz.
                    IdempotencyKey = anahtar

                };

                _context.Orders.Add(siparis);
                await _context.SaveChangesAsync(); // siparis.Id burada üretilir

                // 7) Sipariş detaylarını siparişe bağla ve kaydet
                foreach (var detay in siparisDetaylari)
                {
                    detay.OrderId = siparis.Id;
                    _context.OrderItems.Add(detay);
                }

                // ⭐ YENİ — stok hareketlerini siparişe bağla ve context'e ekle.
                //
                // Bu döngü, hemen üstündeki OrderItem döngüsüyle aynı
                // işi yapıyor: yukarıda üretilip bekletilen nesnelere
                // siparişin Id'sini yazıp context'e koymak.
                //
                // ⚠️ NEDEN BURADA, NEDEN YUKARIDA DEĞİL?
                // siparis.Id, bir üstteki SaveChangesAsync çağrısında
                // veritabanı tarafından üretildi. Ondan önce 0'dı.
                //
                // AddRange: tek tek Add çağırmakla aynı sonucu verir,
                // sadece daha okunaklı.
                foreach (var hareket in stokHareketleri)
                {
                    hareket.ReferansId = siparis.Id;
                }

                _context.StockMovements.AddRange(stokHareketleri);

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

                // ============================================================
                // ⭐ YENİ — 11) SİPARİŞ ALINDI BİLDİRİMİ
                //
                // ⚠️ KONUMU KRİTİK: CommitAsync'ten SONRA.
                //
                // Öncesinde gönderseydik ve sonraki bir adım patlasaydı,
                // transaction geri alınır ama MAİL GERİ ALINAMAZDI.
                // Müşterinin elinde var olmayan bir siparişin onayı kalırdı.
                //
                // Kural: geri alınamaz yan etkiler, geri alınabilir olanların
                // sonrasına konur.
                //
                // ⚠️ GuvenliGonderAsync kendi içinde try/catch yapıyor.
                // Buradaki dış catch bloğu RollbackAsync çağırıyor — mail
                // hatası oraya düşseydi, ZATEN COMMIT EDİLMİŞ bir
                // transaction'ı geri almaya çalışırdık. O da ikinci bir
                // istisna fırlatırdı ve asıl hata kaybolurdu.
                //
                // kullanici.Email'i kullanıyoruz: nesne zaten elimizde
                // (adresi dondururken çekmiştik), ekstra sorgu yok.
                var emailKalemleri = await EmailKalemleriGetirAsync(siparis.Id);

                await _email.GuvenliGonderAsync(
                    _log,
                    kullanici.Email,
                    _sablonlar.SiparisAlindi(siparis, emailKalemleri),
                    "SiparisAlindi");
                // ============================================================

                return Ok(SiparisCevabi(
                    siparis,
                    "Sipariş oluşturuldu ve ödeme alındı biladerim!"));
            }

            // ⭐ YENİ — ÇİFT SİPARİŞ: GERÇEK GARANTİ BURADA
            //
            // Ön kontrolü geçen iki eşzamanlı istek buraya düşer.
            // İkisi de sipariş yazmaya çalışır, unique index birini
            // reddeder ve SaveChangesAsync DbUpdateException fırlatır.
            //
            // ⚠️ Bu catch, genel catch'ten ÖNCE gelmek ZORUNDA.
            //    C# catch bloklarını yukarıdan aşağı dener; genel
            //    olan üstte olsaydı bu blok hiç çalışmazdı.
            //
            // "when (anahtar != null)": anahtarsız isteklerde bu
            // istisna bizimle ilgili olamaz, genel catch'e bıraksın.
            catch (DbUpdateException) when (anahtar != null)
            {
                await transaction.RollbackAsync();

                // Rakip istek commit etti mi? Ona bakıyoruz.
                //
                // Sahiplik yine sorgunun içinde: başka bir kullanıcının
                // siparişini asla döndürmeyiz.
                var kazanan = await _context.Orders
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.IdempotencyKey == anahtar
                                           && o.UserId == userId);

                if (kazanan != null)
                {
                    // Bizim isteğimiz kaybetti ama müşteri açısından
                    // hiçbir şey olmadı: siparişi var, cevabı alıyor.
                    return Ok(SiparisCevabi(
                        kazanan,
                        "Bu sipariş zaten oluşturulmuştu."));
                }

                // Buraya düşersek istisna BAŞKA bir benzersizlik
                // ihlalinden geldi (örneğin sipariş numarası). O zaman
                // bu gerçek bir hatadır, gizlemiyoruz.
                return StatusCode(500, new
                {
                    mesaj = "Sipariş oluşturulurken hata oldu, işlem geri alındı."
                });
            }

            catch (Exception ex)
            {
                // Bir şey patlarsa HER ŞEYİ geri al
                await transaction.RollbackAsync();
                return StatusCode(500, new { mesaj = "Sipariş oluşturulurken hata oldu, işlem geri alındı.", hata = ex.Message });
            }
        }

        // 🟡 GET /api/orders/durum-ozeti — siparişlerimin durum dağılımı
        //
        // ⚠️ NEDEN AYRI BİR UÇ, NEDEN LİSTEDEN SAYILMIYOR?
        //
        // Sipariş listesi sayfalanabilir. İstemci elindeki listeyi
        // gruplayıp saysaydı yalnızca ELİNDEKİ SAYFAYI sayardı ve
        // "Teslim Edildi (5)" yazarken gerçekte 40 tane olurdu.
        // Rakam patlamaz, sessizce yanlış çıkardı — en kötü hata türü.
        //
        // ⚠️ Sayım VERİTABANINDA yapılıyor (GroupBy → SQL COUNT).
        // Siparişleri çekip belleğe alıp orada saymak, 500 siparişi
        // sadece 4 sayı üretmek için ağdan geçirmek olurdu.
        [HttpGet("durum-ozeti")]
        public async Task<IActionResult> GetDurumOzeti()
        {
            var userId = GetUserId();

            var sayimlar = await _context.Orders
                .Where(o => o.UserId == userId)
                .GroupBy(o => o.Status)
                .Select(g => new { Durum = g.Key, Adet = g.Count() })
                .ToListAsync();

            // ⚠️ SIFIR OLAN DURUMLAR DA DÖNÜYOR.
            //
            // GroupBy yalnızca VAR OLAN durumları üretir; hiç iptali
            // olmayan müşteride "iptal" anahtarı hiç gelmezdi.
            // Sözlükten okuyup varsayılan 0 vererek dördünü de
            // garantiliyoruz.
            //
            // Neden gizlemiyoruz? "İptal (0)" görmek müşteriye
            // "iptalim yok" der; satırın hiç olmaması "burada iptal
            // diye bir şey yok mu?" sorusunu doğurur. Boş bir cevap,
            // cevapsızlıktan iyidir.
            //
            // ⚠️ Anahtarlar Order.Status ile BİREBİR aynı yazılmalı;
            // mobildeki durum.js de aynı kodları tanıyor.
            var sozluk = sayimlar.ToDictionary(x => x.Durum, x => x.Adet);

            int Al(string durum) => sozluk.TryGetValue(durum, out var a) ? a : 0;

            return Ok(new
            {
                hazirlaniyor = Al("hazirlaniyor"),
                kargoda = Al("kargoda"),
                teslimEdildi = Al("teslim_edildi"),
                iptal = Al("iptal"),

                // Toplamı da gönderiyoruz — istemci dördünü toplayabilir
                // ama o toplam, ileride yeni bir durum eklendiğinde
                // sessizce eksik kalırdı.
                toplam = sayimlar.Sum(x => x.Adet)
            });
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

                    // ⭐ YENİ — kargo ücreti.
                    //
                    // Liste ucunda göndermek burada DOĞRU: tek bir
                    // decimal, veri yükü yok denecek kadar az. Ürün
                    // açıklamasını liste ucundan çıkarmıştık çünkü o
                    // 2000 karakterlik metindi — kural "her şeyi kes"
                    // değil, "maliyeti faydasından büyük olanı kes".
                    ShippingCost = o.ShippingCost,

                    Total = o.Total,

                    Status = o.Status,
                    PaymentStatus = o.PaymentStatus,
                    CardLast4 = o.CardLast4,

                    CreatedAt = o.CreatedAt,
                    CancelReason = o.CancelReason,
                    CancelledAt = o.CancelledAt,

                    // ⭐ YENİ — kargo bilgileri
                    ShippingCompany = o.ShippingCompany,
                    TrackingNumber = o.TrackingNumber,
                    ShippedAt = o.ShippedAt,
                    DeliveredAt = o.DeliveredAt,
                    CustomerNote = o.CustomerNote,

                    // ⭐ DEĞİŞTİ — Products JOIN'i kaldırıldı.
                    // Ürün adı kalemin içinde donmuş halde duruyor.
                    Items = _context.OrderItems
                        .Where(oi => oi.OrderId == o.Id)
                        .Select(oi => new OrderItemDto
                        {
                            // ⚠️ ARTIK p.Id DEĞİL oi.ProductId.
                            // Ürün silinmişse p diye bir şey yok;
                            // kalemin kendi ProductId'si her zaman var.
                            ProductId = oi.ProductId,

                            ProductName = oi.ProductName,   // ⭐ donmuş ad
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

            // ⭐ DEĞİŞTİ — artık DTO'ya değil ENTITY'ye çekiyoruz.
            //
            // Sebep: KDV dökümü kalemlerin oranına ihtiyaç duyuyor ve
            // aynı veriyi ikinci bir sorguyla çekmenin anlamı yok.
            // Projeksiyon aşağıda, bellekte yapılıyor.
            var kalemler = await _context.OrderItems
                .Where(oi => oi.OrderId == id)
                .ToListAsync();

            var items = kalemler
                .Select(oi => new OrderItemDto
                {
                    ProductId = oi.ProductId,
                    ProductName = oi.ProductName,
                    Quantity = oi.Quantity,
                    UnitPrice = oi.UnitPrice,
                    VatRate = oi.VatRate,         // ⭐ YENİ
                    EskiFiyat = oi.EskiFiyat      // ⭐ YENİ (B1)
                })
                .ToList();

            // ⭐ YENİ — KDV dökümü (kargo dahil).
            var kdvOzeti = SiparisKdvDokumu(order, kalemler);

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

                // ⭐ YENİ — kargo ücreti
                ShippingCost = order.ShippingCost,

                Total = order.Total,

                Status = order.Status,
                PaymentStatus = order.PaymentStatus,
                CardLast4 = order.CardLast4,

                CreatedAt = order.CreatedAt,
                CancelReason = order.CancelReason,
                CancelledAt = order.CancelledAt,

                // ⭐ YENİ — kargo bilgileri
                ShippingCompany = order.ShippingCompany,
                TrackingNumber = order.TrackingNumber,

                // ⭐ YENİ (B7) — takip bağlantısı. Firmanın şablonu
                // tanımlı değilse null gelir ve ekran butonu çizmez.
                TrackingUrl = KargoTakipUrlOlustur(order.ShippingCompany, order.TrackingNumber),

                ShippedAt = order.ShippedAt,
                DeliveredAt = order.DeliveredAt,
                CustomerNote = order.CustomerNote,

                Items = items,

                // ⭐ YENİ — KDV dökümü.
                //
                // ⚠️ Total'a hiçbir şey EKLEMİYOR. Fiyatlar KDV dahil
                // olduğu için Total zaten nihai tutar; bunlar onun
                // içinden ayrıştırılmış bilgilendirme değerleri.
                VatLines = kdvOzeti.Satirlar
                    .Select(s => new VatLineDto
                    {
                        Rate = s.Oran,
                        NetAmount = s.Matrah,
                        VatAmount = s.Vergi,
                        GrossAmount = s.DahilTutar
                    })
                    .ToList(),

                TotalVatBase = kdvOzeti.ToplamMatrah,
                TotalVat = kdvOzeti.ToplamVergi,
                HasVatBreakdown = kdvOzeti.DokumVarMi
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
                // 1) Stoğu geri ver + DEFTERE YAZ
                foreach (var kalem in kalemler)
                {
                    var urun = await _context.Products.FindAsync(kalem.ProductId);

                    if (urun != null)
                    {
                        // ⭐ Hareketi stoğu DEĞİŞTİRMEDEN ÖNCE yazıyoruz.
                        //
                        // Sıra önemli: urun.Stock += ... satırından
                        // sonra yazsaydık "önceki stok" olarak zaten
                        // artmış değeri kaydederdik ve defter
                        // kendi içinde tutarsız olurdu.
                        _defter.Ekle(
                            urunId: urun.Id,
                            miktar: kalem.Quantity,      // iade = artı
                            oncekiStok: urun.Stock,
                            sebep: StokSebep.IptalIadesi,
                            kullaniciId: userId,
                            referansTipi: "Order",
                            referansId: order.Id);

                        // ⚠️ Burada ExecuteUpdate DEĞİL, normal
                        // atama kullanıyoruz — bu bilinçli.
                        //
                        // Yarış koşulu her yazmada olmaz: yazılan
                        // değer okunan değere bağlıysa (x = x + n)
                        // yarış var. Ama burada iki eşzamanlı iptal
                        // aynı siparişi iptal edemez (durum kontrolü
                        // engelliyor), o yüzden risk yok.
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

                // ⭐ YENİ — iptal bildirimi (transaction'dan SONRA).
                //
                // Müşteri iptali kendisi yaptı, "haberi var" diyebiliriz.
                // Yine de gönderiyoruz çünkü mail bir KAYIT işlevi görüyor:
                // iade tutarı, sipariş numarası ve tarih yazılı olarak
                // elinde kalıyor. Bankaya itiraz gerekirse bu belge olur.
                var aliciEmail = await MusteriEmailiGetirAsync(order.UserId);

                await _email.GuvenliGonderAsync(
                    _log,
                    aliciEmail,
                    _sablonlar.SiparisIptalEdildi(order, order.CancelReason),
                    "SiparisIptal:Musteri");

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

        // ⭐ YENİ — KARGO FİRMALARI
        //
        // Listeyi appsettings'ten okur. Koda gömülmez ki firma eklemek
        // için yeni sürüm çıkmak gerekmesin.
        //
        // Get<string[]>() JSON dizisini doğrudan C# dizisine çeviriyor.
        // Bunun için System.Text.Json'a elle dokunmuyoruz — yapılandırma
        // altyapısı bağlamayı (binding) kendi yapıyor.
        //
        // ?? Array.Empty<string>() : ayar hiç tanımlanmamışsa null döner.
        // Boş diziye çevirmek, çağıran her yerde null kontrolü yapma
        // zorunluluğunu ortadan kaldırıyor.
        //
        // Neden static DEĞİL, neden her çağrıda okuyoruz?
        //   _config bir örnek (instance) alanı, static metottan
        //   erişilemez. Ayrıca .NET yapılandırmayı zaten bellekte
        //   tutuyor — burada disk okuması yok, sadece sözlükten değer
        //   alma var. Önbelleğe almanın kazancı yok.
        private string[] KargoFirmalariniGetir()
        {
            return _config.GetSection("Kargo:Firmalar").Get<string[]>()
                   ?? Array.Empty<string>();
        }

        // ⭐ YENİ (B7) — MÜŞTERİYE GÖSTERİLECEK TAKİP BAĞLANTISI
        //
        // Firma adına karşılık gelen şablonu appsettings'ten okur ve
        // takip numarasını yerleştirir. Karşılığı yoksa null döner.
        //
        // Neden bağlantıyı SUNUCU kuruyor, mobil kendisi kurmuyor?
        //   Şablonlar yapılandırmada; mobilin onları bilmesi demek
        //   uygulamayı güncellemeden firma ekleyememek demek. Ayrıca
        //   aynı eşleme ileride e-posta şablonunda da gerekirse tek
        //   yerden gelir.
        //
        // ⚠️ null DÖNMEK BİR HATA DEĞİL, GEÇERLİ BİR CEVAP. Ekran
        // butonu çizmiyor. Tanımsız firmaya uydurma bir adres üretmek,
        // müşteriyi "sayfa bulunamadı"ya göndermek olurdu.
        //
        // ⚠️ Takip numarası URL'e girmeden ÖNCE kaçırılıyor. Numaralar
        // bugün alfanümerik ama adresi biz kuruyorsak kaçırmayı da biz
        // yapmalıyız — veriye güvenip adres birleştirmek klasik bir
        // enjeksiyon yoludur.
        private string? KargoTakipUrlOlustur(string? firma, string? takipNo)
        {
            if (string.IsNullOrWhiteSpace(firma) || string.IsNullOrWhiteSpace(takipNo))
            {
                return null;
            }

            var sablon = _config[$"Kargo:TakipUrlleri:{firma.Trim()}"];

            if (string.IsNullOrWhiteSpace(sablon))
            {
                return null;
            }

            return sablon.Replace("{takipNo}", Uri.EscapeDataString(takipNo.Trim()));
        }

        // 🔴 GET /api/admin/kargo-firmalari — panelin açılır menüsü için
        //
        // Neden ayrı bir endpoint, liste panelde sabit yazılamaz mı?
        //   Yazılabilirdi ama o zaman aynı liste İKİ yerde dururdu:
        //   backend'in doğrulama listesi ve panelin menü listesi.
        //   İkisi ayrışınca admin menüden bir firma seçer, sunucu
        //   "böyle bir firma yok" der. Tek kaynak ilkesi:
        //   liste sunucuda yaşar, panel ondan sorar.
        //
        // Neden "async Task<IActionResult>" değil de düz "IActionResult"?
        //   İçeride beklenecek (await) hiçbir şey yok — ne veritabanı ne
        //   ağ. Gereksiz yere async yazmak derleyici uyarısı üretir ve
        //   her çağrıda ufak bir durum makinesi maliyeti ekler.
        [Authorize(Roles = "admin")]
        [HttpGet("/api/admin/kargo-firmalari")]
        public IActionResult GetKargoFirmalari()
        {
            return Ok(KargoFirmalariniGetir());
        }

        // ⭐ DURUM MAKİNESİ
        // Bir sipariş hangi durumdan hangi duruma geçebilir?
        // Gerçek hayatta sipariş geri gitmez: teslim edilmiş bir sipariş
        // tekrar "hazırlanıyor" olamaz. Bu kuralı burada tanımlıyoruz.
        //
        // hazirlaniyor ──→ kargoda ──→ teslim_edildi  (son)
        //        └──────────────┴──────→ iptal          (son)
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
                    // ⭐ DÜZELTİLDİ — alan adı bozuktu.
                    //
                    // Buraya "tarih" yerine "t" + dört ARAPÇA harf
                    // (ا ر ي خ) yazılmıştı. Muhtemelen klavye/IME
                    // kazası. C# Unicode tanımlayıcılara izin verdiği
                    // için DERLENDİ ve hiçbir uyarı çıkmadı.
                    //
                    // Sonuç: JSON'a "tarih" değil o bozuk adla
                    // çıkıyordu. Panel s.tarih okuyup undefined alıyor,
                    // tarihBicimle(undefined) "-" döndürüyordu.
                    // Yani sipariş listesinde TARİH SÜTUNU HİÇ
                    // ÇALIŞMAMIŞTI — patlamayan, sessiz hata.
                    tarih = x.o.CreatedAt,

                    // ⭐ YENİ — kargo takip bilgisi.
                    //
                    // Ek sorgu maliyeti YOK: bu alanlar zaten Orders
                    // tablosunda, aynı SELECT'e iki kolon daha eklemekten
                    // ibaret. "Detayda var, listede gerekmez" demek için
                    // bir sebep olsaydı (pahalı JOIN, hassas veri)
                    // göndermezdik — ikisi de yok.
                    kargoFirmasi = x.o.ShippingCompany,
                    takipNo = x.o.TrackingNumber,

                    // ⭐ Müşteri notu VAR MI? Metnin kendisini değil,
                    // sadece varlığını gönderiyoruz.
                    //
                    // Neden metni göndermiyoruz? 500 karakterlik notlar
                    // 50 satırlık bir listede 25 KB gereksiz veri eder ve
                    // listede zaten gösterilecek yer yok. Listede lazım
                    // olan bilgi "bu siparişte okunacak bir not var mı" —
                    // bir ikon göstermek için bu yeterli. Metnin tamamı
                    // detay sayfasında zaten geliyor.
                    //
                    // Bu genel bir prensip: liste uçları ÖZET, detay
                    // uçları TAM veri döndürür.
                    notVarMi = x.o.CustomerNote != null,

                    // Kaç ÇEŞİT ürün (satır sayısı)
                    urunCesidi = _context.OrderItems.Count(oi => oi.OrderId == x.o.Id),

                    // Kaç ADET ürün (miktarların toplamı)
                    toplamAdet = _context.OrderItems
                        .Where(oi => oi.OrderId == x.o.Id)
                        .Sum(oi => (int?)oi.Quantity) ?? 0,

                    // ⭐ İLK 2 ÜRÜNÜN ADI — listede önizleme için.
                    //    Tümünü değil sadece 2'sini çekiyoruz; gerisi detayda.
                    //    Bu da alt sorgu olarak TEK SQL'e gömülür (N+1 yok).
                    // ⭐ DEĞİŞTİ — JOIN kaldırıldı.
                    // Bu alt sorgu yine ana SQL'e gömülüyor (N+1 yok),
                    // sadece artık ikinci bir tabloya uğramıyor.
                    ilkUrunler = _context.OrderItems
                        .Where(oi => oi.OrderId == x.o.Id)
                        .Select(oi => oi.ProductName)
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

            // ⭐ DEĞİŞTİ — JOIN kaldırıldı, donmuş ad okunuyor.
            //
            // Ayrıca "maliyet" alanını BİLEREK eklemiyoruz: sipariş
            // detayı ekranı satış bilgisi gösterir, kâr analizi
            // Raporlar sayfasının işi. Aynı veriyi her ekrana serpmek
            // ekranların amacını bulanıklaştırır.
            // ⭐ DEĞİŞTİ — önce entity, sonra projeksiyon.
            // KDV dökümü kalem oranlarına ihtiyaç duyuyor; aynı veriyi
            // ikinci bir sorguyla çekmenin anlamı yok.
            var kalemEntityleri = await _context.OrderItems
                .Where(oi => oi.OrderId == id)
                .ToListAsync();

            var kalemler = kalemEntityleri
                .Select(oi => new
                {
                    urunId = oi.ProductId,
                    urunAdi = oi.ProductName,
                    adet = oi.Quantity,
                    birimFiyat = oi.UnitPrice,
                    araToplam = oi.Quantity * oi.UnitPrice,
                    kdvOrani = oi.VatRate          // ⭐ YENİ
                })
                .ToList();

            // ⭐ YENİ — KDV dökümü (kargo dahil).
            var kdvOzeti = SiparisKdvDokumu(order, kalemEntityleri);

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

                // ⭐ YENİ — kargo ücreti.
                //
                // Anahtar Türkçe: bu uç OrderDto değil anonim nesne
                // döndürüyor ve panelin geri kalanı (siparisNo,
                // kartSon4, kargoFirmasi) Türkçe. Dosya içinde
                // tutarlı olmak, projeye tek bir global kural
                // dayatmaktan önemli.
                kargoUcreti = order.ShippingCost,

                // ⭐ YENİ — KDV DÖKÜMÜ
                //
                // ⚠️ "tutar" alanına hiçbir şey EKLEMİYOR. Fiyatlar KDV
                // dahil olduğu için tutar zaten nihai; bunlar onun
                // içinden ayrıştırılmış bilgilendirme değerleri.
                //
                // varMi false ise panel bu bölümü hiç çizmeyecek —
                // eski siparişlerde oran bilinmiyor ve "KDV: 0,00 TL"
                // yazmak eksik değil YANLIŞ bilgi olurdu.
                kdv = new
                {
                    varMi = kdvOzeti.DokumVarMi,
                    toplamMatrah = kdvOzeti.ToplamMatrah,
                    toplamVergi = kdvOzeti.ToplamVergi,

                    satirlar = kdvOzeti.Satirlar.Select(s => new
                    {
                        oran = s.Oran,
                        matrah = s.Matrah,
                        vergi = s.Vergi,
                        dahilTutar = s.DahilTutar
                    })
                },

                durum = order.Status,
                odemeDurumu = order.PaymentStatus,
                kartSon4 = order.CardLast4,

                iptalSebebi = order.CancelReason,
                iptalTarihi = order.CancelledAt,

                // ⭐ YENİ — kargo bilgileri.
                //
                // Burada Türkçe anahtar kullanıyoruz çünkü bu endpoint
                // OrderDto değil, anonim nesne döndürüyor ve panelin geri
                // kalanı (siparisNo, kartSon4, iptalSebebi) Türkçe.
                // Modeller İngilizce, bu özel admin sözleşmesi Türkçe —
                // dosya içinde tutarlı olmak, projeye global tek bir kural
                // dayatmaktan daha önemli.
                kargoFirmasi = order.ShippingCompany,
                takipNo = order.TrackingNumber,
                kargoyaVerilmeTarihi = order.ShippedAt,
                teslimTarihi = order.DeliveredAt,

                // Kargo hazırlayan bunu MUTLAKA görmeli — panelde
                // belirgin gösterilecek (1.3'te).
                musteriNotu = order.CustomerNote,

                // Panelin "Kargoya Ver" modalındaki menüyü doldurması için.
                // Ayrı bir istek atmasına gerek kalmıyor.
                kargoFirmalari = KargoFirmalariniGetir(),

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
                    email = x.u.Email,

                    // ⭐ YENİ — kargo bilgisi.
                    // Etiket basılırken genelde HENÜZ YOK (takip numarasını
                    // kargocu koliyi aldıktan sonra veriyor). Bu yüzden
                    // etiket bileşeni bu alanları koşullu çiziyor.
                    kargoFirmasi = x.o.ShippingCompany,
                    takipNo = x.o.TrackingNumber,

                    // ⭐ YENİ — MÜŞTERİ NOTU.
                    //
                    // Etiketteki en kritik eklenti. Notu okuması gereken kişi
                    // koliyi hazırlayan kişidir ve o kişi ekrana değil
                    // etikete bakar. Panelde göstermek tek başına yetmez.
                    musteriNotu = x.o.CustomerNote
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

                    // ⭐ YENİ — yukarıdaki sorgudan gelen alanları
                    // ikinci projeksiyona da taşımak ZORUNLU.
                    // Anonim nesneler alan alan yeniden kuruluyor;
                    // burada yazmazsak alan sessizce kaybolur ve
                    // etikette hiç görünmez (hata da vermez).
                    s.kargoFirmasi,
                    s.takipNo,
                    s.musteriNotu,

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
        //
        // ⭐ v6: Artık sadece durum değiştirmiyor. "kargoda" geçişinde
        //        firma + takip numarası da alıyor ve tarihleri yazıyor.
        [Authorize(Roles = "admin")]
        [HttpPut("/api/admin/orders/{id}/status")]
        public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] StatusUpdateDto dto)
        {
            // DTO'daki [MaxLength] gibi öznitelikler burada devreye girer.
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

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

            // ============================================================
            // ⭐ YENİ — KARGOYA VERME KURALLARI
            //
            // Neden bu kontroller DTO özniteliğiyle yapılamadı?
            //   [Required] koşulsuz çalışır: "teslim_edildi" geçişinde de
            //   takip numarası isterdi. Kural alana değil DURUMA bağlı,
            //   dolayısıyla yeri iş mantığı.
            //
            // Neden geçiş kontrolünden SONRA?
            //   Sıra önemli. Önce "bu geçiş yapılabilir mi" diye soruyoruz.
            //   Ters sırada olsaydı, teslim edilmiş bir siparişi tekrar
            //   kargoya vermeye çalışan admin önce "takip numarası gir"
            //   uyarısı alır, numarayı girer, sonra "bu geçiş yapılamaz"
            //   duvarına toslardı. Kullanıcıyı boşuna uğraştırmak.
            // ============================================================
            if (yeniDurum == "kargoda")
            {
                // Trim: admin yapıştırırken başa/sona boşluk gelmesi çok yaygın.
                // Boşluklu takip numarası kargo firmasının sitesinde bulunamaz.
                var firma = dto.ShippingCompany?.Trim();
                var takipNo = dto.TrackingNumber?.Trim();

                if (string.IsNullOrWhiteSpace(firma))
                {
                    return BadRequest(new
                    {
                        mesaj = "Kargoya verirken kargo firmasını seçmelisin."
                    });
                }

                if (string.IsNullOrWhiteSpace(takipNo))
                {
                    return BadRequest(new
                    {
                        mesaj = "Kargoya verirken takip numarası girmelisin."
                    });
                }

                // ---- BEYAZ LİSTE KONTROLÜ ----
                //
                // Panel açılır menü gösteriyor ama menüye güvenmiyoruz:
                // istek Postman'den de gelebilir. "Ön yüz zaten kısıtlıyor"
                // asla bir doğrulama gerekçesi değildir — ön yüz saldırganın
                // kontrolündedir.
                //
                // Neden serbest metin kabul etmiyoruz? Yazım hataları
                // ("Yurtici", "yurtiçi kargo", "YK") veriyi çöpe çevirir.
                // İleride "hangi firmayla kaç gönderi yaptık" raporu
                // istediğimizde aynı firma 5 farklı isimle görünürdü.
                var izinliFirmalar = KargoFirmalariniGetir();

                // Length > 0 koşulu bilinçli: yapılandırma hiç tanımlanmamışsa
                // doğrulamayı ATLIYORUZ.
                //
                // Alternatif "liste boşsa hiçbir şeyi kabul etme" olurdu ama
                // o zaman appsettings'teki tek bir yazım hatası TÜM kargo
                // işlemlerini durdururdu. Yanlış yapılandırmanın bedeli
                // "biraz gevşek doğrulama" olsun, "mağaza kargo veremiyor"
                // olmasın.
                if (izinliFirmalar.Length > 0 &&
                    !izinliFirmalar.Contains(firma, StringComparer.OrdinalIgnoreCase))
                {
                    return BadRequest(new
                    {
                        mesaj = $"'{firma}' tanımlı bir kargo firması değil. " +
                                $"Seçilebilecekler: {string.Join(", ", izinliFirmalar)}"
                    });
                }

                order.ShippingCompany = firma;
                order.TrackingNumber = takipNo;

                // ⭐ Tarihi SUNUCU yazıyor, admin girmiyor.
                //
                // "Ne zaman kargoya verdim" sorusunun cevabı, butona
                // basılan andır. Admin'e tarih girdirseydik:
                //   • yanlış tarih girilebilirdi (kasten veya sehven)
                //   • saat dilimi karmaşası çıkardı
                //   • bir alan daha doldurmak zorunda kalırdı
                //
                // UtcNow: projedeki tüm tarihler UTC. Yerel saat kullanmak
                // yaz saati geçişlerinde sipariş sıralamasını bozardı —
                // bu dersi zaten bir kere yaşadık.
                order.ShippedAt = DateTime.UtcNow;
            }

            if (yeniDurum == "teslim_edildi")
            {
                order.DeliveredAt = DateTime.UtcNow;
            }

            order.Status = yeniDurum;
            await _context.SaveChangesAsync();

            // ============================================================
            // ⭐ YENİ — DURUM BİLDİRİMİ
            //
            // Burada açık bir transaction yok — SaveChangesAsync kendi
            // içinde örtük bir transaction kullanıyor ve dönüş yaptığında
            // veri zaten kalıcı. Yine de maili SONRASINA koyuyoruz;
            // aynı ilke geçerli.
            //
            // Neden tek if içinde iki durum? İkisi de "müşteriye haber
            // ver" işi ve ortak veriye (alıcı e-postası) ihtiyaç duyuyor.
            // Ayrı ayrı yazsaydık e-posta sorgusu iki yerde tekrarlanırdı.
            //
            // "hazirlaniyor" için bildirim YOK: sipariş zaten o durumda
            // oluşuyor ve "Sipariş Alındı" maili gönderilmiş oluyor.
            // İkinci bir mail gürültü olurdu.
            if (yeniDurum == "kargoda" || yeniDurum == "teslim_edildi")
            {
                var aliciEmail = await MusteriEmailiGetirAsync(order.UserId);

                // Şablon seçimi burada; gönderim tek satır.
                // İki ayrı GuvenliGonderAsync çağrısı yazmak yerine
                // sadece İÇERİĞİ dallandırıyoruz — değişen tek şey o.
                var icerik = yeniDurum == "kargoda"
                    ? _sablonlar.KargoyaVerildi(order)
                    : _sablonlar.TeslimEdildi(order);

                // olayAdi'na durumu da ekliyoruz: log'da hangi bildirimin
                // başarısız olduğu tek bakışta görünsün.
                await _email.GuvenliGonderAsync(
                    _log,
                    aliciEmail,
                    icerik,
                    "SiparisDurumu:" + yeniDurum);
            }
            // ============================================================

            // Kargo bilgilerini cevapta geri döndürüyoruz ki panel
            // sayfayı baştan yüklemeden ekranı güncelleyebilsin.
            return Ok(new
            {
                mesaj = "Sipariş durumu güncellendi biladerim!",
                durum = yeniDurum,
                kargoFirmasi = order.ShippingCompany,
                takipNo = order.TrackingNumber,
                kargoyaVerilmeTarihi = order.ShippedAt,
                teslimTarihi = order.DeliveredAt
            });
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
                // 1) STOĞU GERİ VER + DEFTERE YAZ
                // Sipariş verilirken stok düşülmüştü; iptal edilince o ürünler
                // tekrar satılabilir olmalı.
                foreach (var kalem in kalemler)
                {
                    var urun = await _context.Products.FindAsync(kalem.ProductId);

                    if (urun != null)
                    {
                        // ⭐ Hareketi stok DEĞİŞMEDEN ÖNCE yazıyoruz.
                        //
                        // Sıra kritik: urun.Stock += ... satırından sonra
                        // yazsaydık "önceki stok" olarak zaten artmış
                        // değeri kaydederdik. Defter kendi içinde
                        // tutarsız olur, "önceki + miktar = sonraki"
                        // eşitliği bozulurdu.
                        //
                        // ⚠️ Burada Ekle() kullanıyoruz, Olustur() değil.
                        // Fark: siparişin Id'si ZATEN VAR (iptal edilen
                        // sipariş çoktan kaydedilmiş), referansı baştan
                        // verebiliyoruz. Erteleme gerekmiyor.
                        //
                        // ⚠️ kullaniciId burada ADMİN'in id'si, siparişin
                        // sahibinin değil. Defterin sorusu "kim yaptı",
                        // "kime yapıldı" değil. Müşteri iptalinde müşteri,
                        // admin iptalinde admin yazılıyor — bunlar farklı
                        // olaylar ve denetimde ayırt edilebilmeli.
                        _defter.Ekle(
                            urunId: urun.Id,
                            miktar: kalem.Quantity,      // iade = artı
                            oncekiStok: urun.Stock,
                            sebep: StokSebep.IptalIadesi,
                            kullaniciId: GetUserId(),
                            referansTipi: "Order",
                            referansId: order.Id,
                            aciklama: "Admin tarafından iptal edildi");

                        // ⚠️ Burada ExecuteUpdate DEĞİL, normal atama
                        // kullanıyoruz — bu bilinçli.
                        //
                        // Yarış koşulu her yazmada olmaz: yazılan değer
                        // okunan değere bağlıysa (x = x + n) yarış var.
                        // Ama iki eşzamanlı iptal aynı siparişi iptal
                        // edemez — yukarıdaki durum kontrolü engelliyor.
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

                // ⭐ YENİ — iptal bildirimi (transaction'dan SONRA).
                //
                // Bu, dört bildirim içinde en ÖNEMLİSİ: müşterinin
                // haberi olmadan siparişi iptal edildi. Uygulamayı
                // açmazsa günlerce beklemeye devam eder.
                //
                // Şablonda iptal sebebi de gidiyor — admin'in yazdığı
                // metin doğrudan müşteriye ulaşıyor. Bu yüzden şablonda
                // Kacir() ile HTML kaçışı yapıyoruz; admin de sonuçta
                // serbest metin giriyor.
                var aliciEmail = await MusteriEmailiGetirAsync(order.UserId);

                await _email.GuvenliGonderAsync(
                    _log,
                    aliciEmail,
                    _sablonlar.SiparisIptalEdildi(order, order.CancelReason),
                    "SiparisIptal:Admin");

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