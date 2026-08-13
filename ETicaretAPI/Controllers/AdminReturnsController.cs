using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;
using ETicaretAPI.Services;

namespace ETicaretAPI.Controllers
{
    // ⭐ YENİ (Aşama 9) — İADE TALEPLERİ (ADMİN TARAFI)
    //
    //   talep_edildi → onaylandi → teslim_alindi → para_iade_edildi
    //               ↘ reddedildi
    //
    // ⚠️ Her adım ayrı uç: son adım para hareketi. Serbest bir "durumu
    // şu yap" ucu, parayı ödemeden ödendi yazmaya izin verirdi.
    [Route("api/admin/iadeler")]
    [ApiController]
    [Authorize(Roles = "admin")]
    public class AdminReturnsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IadeHesaplayici _hesap;
        private readonly StokDefteri _defter;
        private readonly IEmailGonderici _email;
        private readonly EmailSablonlari _sablonlar;
        private readonly ILogger<AdminReturnsController> _log;

        // ⭐ YENİ — denetim kaydı. Para iadesi bugüne kadar HİÇ kayda
        // geçmiyordu: gerçek para hareketi yaratan bir işlemin "kim
        // yaptı" cevabı yoktu.
        private readonly DenetimKaydi _denetim;

        public AdminReturnsController(
            AppDbContext context,
            IadeHesaplayici hesap,
            StokDefteri defter,
            IEmailGonderici email,
            EmailSablonlari sablonlar,
            ILogger<AdminReturnsController> log,
            DenetimKaydi denetim)
        {
            _context = context;
            _hesap = hesap;
            _defter = defter;
            _email = email;
            _sablonlar = sablonlar;
            _log = log;
            _denetim = denetim;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // ⭐ DURUM MAKİNESİ (OrdersController'daki desen).
        // ⚠️ Adımların yan etkisi geri alınamaz: stok rafa dönüyor,
        // para çıkıyor. Geriye dönüş yok.
        private static readonly Dictionary<string, string[]> GecerliGecisler =
            new()
            {
                [IadeDurumu.TalepEdildi] = new[] { IadeDurumu.Onaylandi, IadeDurumu.Reddedildi },
                [IadeDurumu.Onaylandi] = new[] { IadeDurumu.TeslimAlindi },
                [IadeDurumu.TeslimAlindi] = new[] { IadeDurumu.ParaIadeEdildi },
                [IadeDurumu.ParaIadeEdildi] = Array.Empty<string>(),   // son
                [IadeDurumu.Reddedildi] = Array.Empty<string>()        // son
            };

        private static bool GecisGecerliMi(string mevcut, string yeni)
        {
            return GecerliGecisler.TryGetValue(mevcut, out var izinliler)
                && izinliler.Contains(yeni);
        }

        // ⭐ YENİ — "para iadesine hangi durumdan geçilebilir?"
        //
        // ⚠️ Elle `IadeDurumu.TeslimAlindi` yazmak yerine durum
        // makinesinden TÜRETİYORUZ. Sebep: bu liste aşağıdaki atomik
        // UPDATE'in WHERE koşuluna giriyor. Elle yazsaydık kural iki
        // yerde yaşardı ve yarın geçiş tablosu değiştiğinde biri
        // güncellenip diğeri sessizce eski kalırdı — üstelik en kötü
        // yerde: para ödeyen sorgunun koşulunda.
        //
        // ⚠️ Bildirim sırası önemli: GecerliGecisler'den SONRA
        // gelmeli, statik alanlar yazıldıkları sırayla ilklenir.
        private static readonly string[] ParaIadesineGecebilenDurumlar =
            GecerliGecisler
                .Where(x => x.Value.Contains(IadeDurumu.ParaIadeEdildi))
                .Select(x => x.Key)
                .ToArray();

        // 🔴 GET /api/admin/iadeler?durum=talep_edildi&sayfa=1
        [HttpGet]
        public async Task<IActionResult> Liste(
            [FromQuery] string? durum,
            [FromQuery] int sayfa = 1,
            [FromQuery] int sayfaBoyutu = 20)
        {
            if (sayfa < 1) sayfa = 1;
            if (sayfaBoyutu < 1 || sayfaBoyutu > 100) sayfaBoyutu = 20;

            var query = _context.ReturnRequests.AsQueryable();

            // Tanımadığımız filtre sessizce atlanıyor.
            if (!string.IsNullOrWhiteSpace(durum))
            {
                query = query.Where(r => r.Durum == durum);
            }

            var toplam = await query.CountAsync();

            var satirlar = await (
                from r in query
                join o in _context.Orders on r.OrderId equals o.Id
                orderby r.TalepTarihi descending, r.Id descending
                select new { r, o }
            )
            .Skip((sayfa - 1) * sayfaBoyutu)
            .Take(sayfaBoyutu)
            .ToListAsync();

            // Kalemler tek sorguda — satır başına sorgu atmıyoruz.
            var kalemIdler = satirlar
                .Where(x => x.r.OrderItemId.HasValue)
                .Select(x => x.r.OrderItemId!.Value)
                .Distinct()
                .ToList();

            var kalemler = await _context.OrderItems
                .Where(oi => kalemIdler.Contains(oi.Id))
                .ToListAsync();

            var liste = satirlar.Select(x =>
            {
                var kalem = x.r.OrderItemId.HasValue
                    ? kalemler.FirstOrDefault(k => k.Id == x.r.OrderItemId.Value)
                    : null;

                return new IadeOzetDto
                {
                    Id = x.r.Id,
                    OrderId = x.r.OrderId,
                    SiparisNo = x.o.OrderNumber,
                    OrderItemId = x.r.OrderItemId,
                    UrunAdi = kalem?.ProductName,
                    Sebep = x.r.Sebep,
                    Aciklama = x.r.Aciklama,
                    Durum = x.r.Durum,
                    TalepTarihi = x.r.TalepTarihi,
                    KararTarihi = x.r.KararTarihi,
                    RedNedeni = x.r.RedNedeni,
                    Tutar = _hesap.Hesapla(x.o, kalem),
                    IadeTutari = x.r.IadeTutari,

                    // Dondurulmuş ad — canlı kullanıcı adı değil.
                    MusteriAdi = x.o.ShippingFullName
                };
            }).ToList();

            // ⚠️ Sekme sayıları filtresiz sorgudan: filtreliyse
            // diğer sekmelerin sayısı sıfırlanırdı.
            var sayilar = await _context.ReturnRequests
                .GroupBy(r => r.Durum)
                .Select(g => new { durum = g.Key, adet = g.Count() })
                .ToListAsync();

            int Say(string d) => sayilar.FirstOrDefault(x => x.durum == d)?.adet ?? 0;

            return Ok(new
            {
                talepler = liste,
                toplam,
                sayfa,
                sayfaBoyutu,
                toplamSayfa = (int)Math.Ceiling(toplam / (double)sayfaBoyutu),

                durumSayilari = new
                {
                    talepEdildi = Say(IadeDurumu.TalepEdildi),
                    onaylandi = Say(IadeDurumu.Onaylandi),
                    teslimAlindi = Say(IadeDurumu.TeslimAlindi),
                    paraIadeEdildi = Say(IadeDurumu.ParaIadeEdildi),
                    reddedildi = Say(IadeDurumu.Reddedildi)
                }
            });
        }

        // 🔴 PUT /api/admin/iadeler/5/karar — onayla / reddet
        [HttpPut("{id}/karar")]
        public async Task<IActionResult> Karar(int id, [FromBody] IadeKararDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var talep = await _context.ReturnRequests.FirstOrDefaultAsync(r => r.Id == id);

            if (talep == null)
            {
                return NotFound(new { mesaj = "İade talebi bulunamadı!" });
            }

            var yeniDurum = dto.Onay ? IadeDurumu.Onaylandi : IadeDurumu.Reddedildi;

            if (!GecisGecerliMi(talep.Durum, yeniDurum))
            {
                return BadRequest(new
                {
                    mesaj = $"Bu talep artık '{talep.Durum}' durumunda; karar verilemez."
                });
            }

            // ⚠️ Red nedeni zorunlu: müşteri sebebini bilmeli.
            if (!dto.Onay && string.IsNullOrWhiteSpace(dto.RedNedeni))
            {
                return BadRequest(new { mesaj = "Red nedeni yazmalısın." });
            }

            talep.Durum = yeniDurum;
            talep.KararTarihi = DateTime.UtcNow;
            talep.KararVerenUserId = GetUserId();
            talep.RedNedeni = dto.Onay ? null : dto.RedNedeni!.Trim();

            await _context.SaveChangesAsync();

            // Mail transaction dışında: gönderilemedi diye karar
            // geri alınmamalı.
            await KararMailiGonderAsync(talep);

            return Ok(new
            {
                mesaj = dto.Onay
                    ? "İade onaylandı. Müşteriden ürünü göndermesi bekleniyor."
                    : "İade reddedildi.",
                durum = talep.Durum
            });
        }

        // 🔴 PUT /api/admin/iadeler/5/teslim-alindi
        // ⚠️ Ayrı adım: para iadesi paketin gelmesine bağlı.
        [HttpPut("{id}/teslim-alindi")]
        public async Task<IActionResult> TeslimAlindi(int id)
        {
            var talep = await _context.ReturnRequests.FirstOrDefaultAsync(r => r.Id == id);

            if (talep == null)
            {
                return NotFound(new { mesaj = "İade talebi bulunamadı!" });
            }

            if (!GecisGecerliMi(talep.Durum, IadeDurumu.TeslimAlindi))
            {
                return BadRequest(new
                {
                    mesaj = "Bu adım yalnızca onaylanmış bir iadede yapılabilir."
                });
            }

            talep.Durum = IadeDurumu.TeslimAlindi;
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Ürün teslim alındı olarak işaretlendi.", durum = talep.Durum });
        }

        // 🔴 POST /api/admin/iadeler/5/para-iadesi
        // PUT değil POST: idempotent değil, para hareketi yaratıyor.
        //
        // ⭐ DEĞİŞTİ — durum geçişi artık ATOMİK.
        //
        // ⚠️ ESKİ KOD YARIŞ KOŞULUNA AÇIKTI:
        //       var talep = ...FirstOrDefaultAsync(...);   // OKU
        //       if (!GecisGecerliMi(talep.Durum, ...)) ... // KONTROL
        //       ... transaction ... talep.Durum = ...      // YAZ
        //
        //   Üstteki yorumda "ikinci çağrıyı durum makinesi reddediyor"
        //   yazıyordu ve bu SIRALI ikinci çağrı için doğruydu;
        //   EŞZAMANLI ikinci çağrı için değildi. İki istek aynı anda
        //   gelirse ikisi de "teslim_alindi" okur, ikisi de kontrolü
        //   geçer ve ikisi de para öder: iki `Payment("iade")` satırı,
        //   iki kez stok girişi. Ödemeler raporundaki iade toplamı
        //   ikiye katlanır.
        //
        //   Çözüm stok ve kupon sayacındaki desenin aynısı: kontrol ve
        //   yazma TEK cümlede, satır kilidi altında (bkz.
        //   OrdersController — kupon satırı MUTEX olarak kullanılıyor).
        [HttpPost("{id}/para-iadesi")]
        public async Task<IActionResult> ParaIadesi(int id)
        {
            // ⚠️ AsNoTracking — bu nesneyi EF'in takip etmesini
            // İSTEMİYORUZ. Durumu aşağıda ExecuteUpdateAsync ile
            // yazacağız; takip edilseydi SaveChanges aynı kolonlara
            // ikinci bir UPDATE daha gönderirdi.
            // ("ExecuteUpdate ile yazdığın kolona bellekte dokunma"
            //  kuralının bu metottaki karşılığı.)
            var talep = await _context.ReturnRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id);

            if (talep == null)
            {
                return NotFound(new { mesaj = "İade talebi bulunamadı!" });
            }

            // ⚠️ Bu kontrol GARANTİ DEĞİL, sadece ucuz durumu ucuza
            // halleder ve anlaşılır bir mesaj verir. Bağlayıcı kontrol
            // aşağıdaki atomik UPDATE'in WHERE koşulu.
            if (!GecisGecerliMi(talep.Durum, IadeDurumu.ParaIadeEdildi))
            {
                return BadRequest(new
                {
                    mesaj = "Para iadesi yalnızca teslim alınmış bir iade için yapılabilir."
                });
            }

            var siparis = await _context.Orders.FirstOrDefaultAsync(o => o.Id == talep.OrderId);

            if (siparis == null)
            {
                return BadRequest(new { mesaj = "Siparişe ulaşılamadı!" });
            }

            // İade edilecek kalemler: tek kalem ya da siparişin hepsi.
            var kalemler = talep.OrderItemId.HasValue
                ? await _context.OrderItems
                    .Where(oi => oi.Id == talep.OrderItemId.Value)
                    .ToListAsync()
                : await _context.OrderItems
                    .Where(oi => oi.OrderId == siparis.Id)
                    .ToListAsync();

            var tutar = _hesap.Hesapla(
                siparis,
                talep.OrderItemId.HasValue ? kalemler.FirstOrDefault() : null);

            var adminId = GetUserId();
            var paraIadeTarihi = DateTime.UtcNow;

            // ⚠️ Stok + para + sipariş durumu tek transaction'da:
            // biri olup diğeri olmazsa kasa ile raf tutmaz.
            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                // ---- 0) DURUMU ATOMİK OLARAK TÜKET ----
                //
                // Ürettiği SQL:
                //     UPDATE ReturnRequests
                //     SET Durum = 'para_iade_edildi', IadeTutari = @t,
                //         ParaIadeTarihi = @tarih
                //     WHERE Id = @id AND Durum IN ('teslim_alindi')
                //
                // ⚠️ BU BLOK TRANSACTION'IN İLK İŞLEMİ OLMAK ZORUNDA.
                // Talep satırına exclusive kilit koyuyor ve kilit
                // COMMIT'e kadar duruyor; aynı talebe gelen ikinci
                // istek tam burada kuyruğa giriyor. Stoktan sonra
                // yapsaydık rakip istek stoğu çoktan iki kez artırmış
                // olurdu.
                //
                // ⚠️ Tutar da BURADA yazılıyor: "para ödendi" ile
                // "ne kadar ödendi" tek atomik adımda kayda geçmeli,
                // yoksa arada çöken bir istek tutarsız satır bırakır.
                var etkilenen = await _context.ReturnRequests
                    .Where(r => r.Id == id
                             && ParaIadesineGecebilenDurumlar.Contains(r.Durum))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Durum, IadeDurumu.ParaIadeEdildi)
                        .SetProperty(r => r.IadeTutari, tutar)
                        .SetProperty(r => r.ParaIadeTarihi, paraIadeTarihi));

                if (etkilenen == 0)
                {
                    // Koşul tutmadı → rakip bir istek bizden önce
                    // ödemeyi yaptı (ya da talep bu arada reddedildi).
                    // Rollback'i açıkça çağırıyoruz ki kilitler metot
                    // bitmesini beklemeden serbest kalsın.
                    await tx.RollbackAsync();

                    return BadRequest(new
                    {
                        mesaj = "Bu iadenin parası zaten ödenmiş görünüyor. Sayfayı yenile."
                    });
                }

                // ---- 1) STOK GERİ ----
                foreach (var kalem in kalemler)
                {
                    var urun = await _context.Products.FindAsync(kalem.ProductId);

                    if (urun == null)
                    {
                        // ⚠️ Ürün silinmişse stok eklenmiyor ama para
                        // iadesi devam ediyor.
                        continue;
                    }

                    // ⚠️ Hareket stok değişmeden ÖNCE yazılıyor,
                    // yoksa "önceki stok" yanlış kaydedilir.
                    _defter.Ekle(
                        urunId: urun.Id,
                        miktar: kalem.Quantity,          // iade = artı
                        oncekiStok: urun.Stock,
                        sebep: StokSebep.Iade,           // iptal iadesinden AYRI sebep
                        kullaniciId: adminId,
                        referansTipi: "ReturnRequest",
                        referansId: talep.Id);

                    urun.Stock += kalem.Quantity;
                }

                // ---- 2) PARA ----
                //
                // ⚠️ Mevcut ödeme satırı çevrilmiyor, YENİ iade satırı
                // ekleniyor (iptal akışından farklı): iade kısmi
                // olabilir, satırı çevirmek tutarın tamamını iade
                // edilmiş gösterirdi.
                _context.Payments.Add(new Payment
                {
                    OrderId = siparis.Id,
                    UserId = siparis.UserId,
                    Amount = tutar,
                    CardLast4 = siparis.CardLast4,   // hangi karta iade edildi
                    Status = "iade",

                    // ⚠️ Yukarıdaki ParaIadeTarihi ile AYNI değişken.
                    // İki kez DateTime.UtcNow çağırsaydık ödeme satırı
                    // ile iade kaydı milisaniye farkla ayrışırdı ve
                    // "aynı işlem mi?" sorusu tarihten cevaplanamazdı.
                    PaidAt = paraIadeTarihi
                });

                // ---- 3) SİPARİŞİN ÖDEME DURUMU ----
                // ⚠️ Kısmi iadede "kismi_iade": "iade_edildi" yazmak
                // siparişin tamamı iade edilmiş gibi gösterirdi.
                siparis.PaymentStatus = talep.OrderItemId.HasValue
                    ? "kismi_iade"
                    : "iade_edildi";

                // ⚠️ Order.Status değişmiyor: teslim edilmiş sipariş
                // teslim edilmiş kalır, iade sonraki bir olay.
                // ⚠️ Kupon UsedCount azalmıyor (karar #6).

                // ⚠️ talep.Durum / IadeTutari / ParaIadeTarihi BURADA
                // YAZILMIYOR — üçü de yukarıdaki atomik UPDATE'te
                // yazıldı. Burada tekrar atasaydık ya ikinci bir UPDATE
                // giderdi ya da (AsNoTracking olduğu için) hiç
                // gitmezdi; ikisi de okuyanı yanıltırdı.

                // ---- 4) DENETİM KAYDI ----
                //
                // ⚠️ Para hareketi yaratan bir işlem iz bırakmadan
                // olmamalı. Yorum gizlemek için tutulan kaydın çok daha
                // fazlası burada gerekli: bu işlem müşterinin hesabına
                // para gönderiyor.
                //
                // ⚠️ AYNI TRANSACTION'DA, aşağıdaki SaveChanges ile
                // birlikte yazılıyor. Ayrı kaydetseydik iade geri
                // alındığında "para iade edildi" diyen sahipsiz bir
                // denetim satırı kalırdı.
                await _denetim.EkleAsync(
                    yapanId: adminId,
                    hedefId: siparis.UserId,
                    hedefAd: $"Sipariş {siparis.OrderNumber}",
                    islem: DenetimIslemi.ParaIadesi,
                    eski: talep.Durum,
                    yeni: $"{tutar:N2} TL iade edildi (talep #{talep.Id})");

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;   // global middleware yakalar
            }

            // ⚠️ Bellekteki kopyayı ŞİMDİ güncelliyoruz.
            //
            // talep AsNoTracking ile okundu, yani bu atamalar hiçbir
            // SQL üretmiyor — sadece aşağıdaki mail şablonu ve cevap
            // doğru değerleri görsün diye. Yapmasaydık müşteriye
            // "iaden teslim alındı" maili giderdi; oysa parası ödendi.
            talep.Durum = IadeDurumu.ParaIadeEdildi;
            talep.IadeTutari = tutar;
            talep.ParaIadeTarihi = paraIadeTarihi;

            await KararMailiGonderAsync(talep);

            return Ok(new
            {
                mesaj = "Para iadesi yapıldı.",
                durum = talep.Durum,
                tutar
            });
        }

        // Mail gönderimi tek yerde: üç uç da aynı şablonu kullanıyor.
        private async Task KararMailiGonderAsync(ReturnRequest talep)
        {
            var bilgi = await (
                from o in _context.Orders
                where o.Id == talep.OrderId
                join u in _context.Users on o.UserId equals u.Id
                select new { o, u.Email }
            ).FirstOrDefaultAsync();

            if (bilgi == null)
            {
                return;
            }

            await _email.GuvenliGonderAsync(
                _log,
                bilgi.Email,
                _sablonlar.IadeDurumBildirimi(bilgi.o, talep),
                "Iade:" + talep.Durum);
        }
    }
}
