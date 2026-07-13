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
                Items = items
            };

            return Ok(dto);
        }




        // 🔴 GET /api/admin/orders — TÜM siparişler (admin)
        [Authorize(Roles = "admin")]
        [HttpGet("/api/admin/orders")]
        public async Task<IActionResult> GetAllOrders()
        {
            var orders = await _context.Orders
                .OrderByDescending(o => o.Id)
                .Select(o => new
                {
                    o.Id,
                    o.UserId,
                    o.Total,
                    o.Status,
                    o.PaymentStatus,
                    o.CardLast4
                })
                .ToListAsync();

            return Ok(orders);
        }

        // 🔴 PUT /api/admin/orders/5/status — kargo durumu değiştir (admin)
        [Authorize(Roles = "admin")]
        [HttpPut("/api/admin/orders/{id}/status")]
        public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] StatusUpdateDto dto)
        {
            var order = await _context.Orders.FindAsync(id);

            if (order == null)
            {
                return NotFound(new { mesaj = "Sipariş bulunamadı!" });
            }

            order.Status = dto.Status;
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = $"Sipariş durumu '{dto.Status}' olarak güncellendi!" });
        }


    }
}