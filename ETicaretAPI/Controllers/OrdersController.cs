using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;

namespace ETicaretAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class OrdersController : ControllerBase
    {
        private readonly AppDbContext _context;

        public OrdersController(AppDbContext context)
        {
            _context = context;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
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

            // 3) Sepeti al (ürün bilgisiyle birlikte)
            var sepetOgeleri = await _context.CartItems
                .Where(ci => ci.UserId == userId)
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

                // 5) Her sepet öğesi için: ürünü bul, stok kontrol et, fiyatı dondur
                foreach (var oge in sepetOgeleri)
                {
                    var urun = await _context.Products.FindAsync(oge.ProductId);

                    if (urun == null)
                    {
                        return BadRequest(new { mesaj = $"Ürün bulunamadı (id: {oge.ProductId})" });
                    }

                    // Stok yeterli mi?
                    if (urun.Stock < oge.Quantity)
                    {
                        return BadRequest(new { mesaj = $"'{urun.Name}' için yeterli stok yok! (stok: {urun.Stock})" });
                    }

                    // Stoğu düş
                    urun.Stock -= oge.Quantity;

                    // Sipariş detayı oluştur — FİYATI DONDUR (o anki fiyat)
                    siparisDetaylari.Add(new OrderItem
                    {
                        ProductId = urun.Id,
                        Quantity = oge.Quantity,
                        UnitPrice = urun.Price // o anki fiyat sabitlenir
                    });

                    toplamTutar += urun.Price * oge.Quantity;
                }

                // 6) Sipariş üst bilgisini oluştur
                var siparis = new Order
                {
                    UserId = userId,
                    AddressId = dto.AddressId,
                    Total = toplamTutar,
                    Status = "hazirlaniyor",
                    PaymentStatus = "odendi",           // ödeme simüle: başarılı
                    CardLast4 = kart.Last4Digits         // kullanılan kartı dondur
                };

                _context.Orders.Add(siparis);
                await _context.SaveChangesAsync(); // siparis.Id burada üretilir

                // 7) Sipariş detaylarını siparişe bağla ve kaydet
                foreach (var detay in siparisDetaylari)
                {
                    detay.OrderId = siparis.Id;
                    _context.OrderItems.Add(detay);
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
                    toplamAdet = _context.OrderItems
                        .Where(oi => oi.OrderId == x.o.Id)
                        .Sum(oi => (int?)oi.Quantity) ?? 0
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

            var adres = await _context.Addresses
                .Where(a => a.Id == order.AddressId)
                .Select(a => new { a.Title, a.FullAddress, a.City })
                .FirstOrDefaultAsync();

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
                tarih = order.CreatedAt,
                tutar = order.Total,
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