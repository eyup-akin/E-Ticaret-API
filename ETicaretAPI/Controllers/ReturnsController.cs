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
    // ⭐ YENİ (Aşama 9) — İADE TALEPLERİ (MÜŞTERİ TARAFI)
    // Admin uçları AdminReturnsController'da: buradaki her uçta
    // sahiplik kontrolü şart, orada değil.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ReturnsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly MagazaAyarlari _ayarlar;
        private readonly IadeHesaplayici _hesap;

        public ReturnsController(
            AppDbContext context,
            MagazaAyarlari ayarlar,
            IadeHesaplayici hesap)
        {
            _context = context;
            _ayarlar = ayarlar;
            _hesap = hesap;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // 🟡 POST /api/returns — iade talebi aç
        [HttpPost]
        public async Task<IActionResult> TalepAc([FromBody] IadeTalepDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetUserId();

            // ⚠️ Beyaz liste sunucuda: menü göstermek yetmez.
            if (!IadeSebebi.Gecerliler.Contains(dto.Sebep))
            {
                return BadRequest(new { mesaj = "Geçersiz iade sebebi!" });
            }

            // Sahiplik kontrolü sorgunun içinde.
            var siparis = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == dto.OrderId && o.UserId == userId);

            if (siparis == null)
            {
                // 403 değil 404: varlığı sızdırmasın.
                return NotFound(new { mesaj = "Sipariş bulunamadı!" });
            }

            // ⚠️ Yalnızca teslim edilmiş sipariş iade edilir.
            // Teslim edilmemişin adı iptal ve ayrı bir akış.
            if (siparis.Status != SiparisDurumlari.TeslimEdildi)
            {
                return BadRequest(new
                {
                    mesaj = "Yalnızca teslim edilmiş siparişler için iade talebi açabilirsin. " +
                            "Henüz teslim edilmediyse siparişi iptal edebilirsin."
                });
            }

            // Süre teslim tarihinden işliyor (cayma hakkı öyle başlar).
            // ⚠️ DeliveredAt eski kayıtlarda null; o zaman sipariş
            // tarihine düşüyoruz — katı yönde, uydurma tarih yerine.
            var baslangic = siparis.DeliveredAt ?? siparis.CreatedAt;
            var sonGun = baslangic.AddDays(_ayarlar.IadeGunSayisi);

            if (DateTime.UtcNow > sonGun)
            {
                return BadRequest(new
                {
                    mesaj = $"İade süresi doldu. Teslimattan sonra {_ayarlar.IadeGunSayisi} " +
                            "gün içinde iade talebi açabilirsin."
                });
            }

            // ---- KALEM KONTROLÜ ----
            OrderItem? kalem = null;

            if (dto.OrderItemId.HasValue)
            {
                // ⚠️ Kalem bu siparişe ait mi? Değilse başkasının
                // ürünü iade edilirdi.
                kalem = await _context.OrderItems
                    .FirstOrDefaultAsync(oi => oi.Id == dto.OrderItemId.Value
                                            && oi.OrderId == siparis.Id);

                if (kalem == null)
                {
                    return BadRequest(new { mesaj = "Geçerli bir sipariş kalemi seçmelisin!" });
                }
            }

            // ⚠️ İndeks (OrderId, OrderItemId) çiftine bakıyor; "tümü"
            // talebi ile kalem talebi farklı çift olduğu için ikisi de
            // geçer. Mantıksal çakışmayı burada eliyoruz.
            var mesgulDurumlar = IadeDurumu.Acikkalanlar
                .Append(IadeDurumu.ParaIadeEdildi)
                .ToArray();

            var mevcutTalepler = await _context.ReturnRequests
                .Where(r => r.OrderId == siparis.Id && mesgulDurumlar.Contains(r.Durum))
                .Select(r => r.OrderItemId)
                .ToListAsync();

            if (mevcutTalepler.Any(x => x == null))
            {
                return Conflict(new
                {
                    mesaj = "Bu siparişin tamamı için zaten bir iade talebi var."
                });
            }

            if (dto.OrderItemId == null && mevcutTalepler.Count > 0)
            {
                return Conflict(new
                {
                    mesaj = "Bu siparişteki bazı ürünler için iade talebi var. " +
                            "Kalan ürünleri tek tek iade edebilirsin."
                });
            }

            var talep = new ReturnRequest
            {
                OrderId = siparis.Id,
                OrderItemId = dto.OrderItemId,
                Sebep = dto.Sebep,
                Aciklama = string.IsNullOrWhiteSpace(dto.Aciklama) ? null : dto.Aciklama.Trim(),
                Durum = IadeDurumu.TalepEdildi,
                TalepTarihi = DateTime.UtcNow
            };

            _context.ReturnRequests.Add(talep);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Önce sormak yerine yazıp ihlali yakalıyoruz:
                // eşzamanlı iki istek arasında yarış kalmasın.
                return Conflict(new
                {
                    mesaj = "Bu ürün için zaten devam eden bir iade talebin var."
                });
            }

            return Ok(new
            {
                mesaj = "İade talebin alındı.",
                id = talep.Id,
                tutar = _hesap.Hesapla(siparis, kalem)
            });
        }

        // 🟡 GET /api/returns — iade taleplerim
        [HttpGet]
        public async Task<IActionResult> Taleplerim()
        {
            var userId = GetUserId();

            // Sahiplik sipariş üzerinden: ReturnRequest'te UserId
            // yok, ikinci kopya ayrışabilirdi.
            var satirlar = await (
                from r in _context.ReturnRequests
                join o in _context.Orders on r.OrderId equals o.Id
                where o.UserId == userId
                orderby r.TalepTarihi descending, r.Id descending
                select new { r, o }
            ).ToListAsync();

            // Kalemler tek seferde — satır başına sorgu atmıyoruz.
            var kalemIdler = satirlar
                .Where(x => x.r.OrderItemId.HasValue)
                .Select(x => x.r.OrderItemId!.Value)
                .Distinct()
                .ToList();

            var kalemler = await _context.OrderItems
                .Where(oi => kalemIdler.Contains(oi.Id))
                .ToListAsync();

            var sonuc = satirlar.Select(x =>
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

                    // Dondurulmuş ürün adı: ürün adı sonradan değişse
                    // bile iade kaydı ne iade edildiğini söylemeli.
                    UrunAdi = kalem?.ProductName,

                    Sebep = x.r.Sebep,
                    Aciklama = x.r.Aciklama,
                    Durum = x.r.Durum,
                    TalepTarihi = x.r.TalepTarihi,
                    KararTarihi = x.r.KararTarihi,
                    RedNedeni = x.r.RedNedeni,

                    Tutar = _hesap.Hesapla(x.o, kalem),
                    IadeTutari = x.r.IadeTutari
                };
            }).ToList();

            return Ok(sonuc);
        }

        // 🟡 GET /api/returns/uygunluk/5 — iade edilebilir mi?
        //
        // Kural tek yerde dursun diye ayrı uç: mobil butonu çizip
        // çizmeyeceğini soruyor. Gerçek kilit POST ucunda.
        [HttpGet("uygunluk/{orderId}")]
        public async Task<IActionResult> Uygunluk(int orderId)
        {
            var userId = GetUserId();

            var siparis = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

            if (siparis == null)
            {
                return NotFound(new { mesaj = "Sipariş bulunamadı!" });
            }

            if (siparis.Status != SiparisDurumlari.TeslimEdildi)
            {
                return Ok(new
                {
                    uygunMu = false,
                    sebep = "Yalnızca teslim edilmiş siparişler iade edilebilir."
                });
            }

            var baslangic = siparis.DeliveredAt ?? siparis.CreatedAt;
            var sonGun = baslangic.AddDays(_ayarlar.IadeGunSayisi);

            if (DateTime.UtcNow > sonGun)
            {
                return Ok(new
                {
                    uygunMu = false,
                    sebep = $"İade süresi doldu ({_ayarlar.IadeGunSayisi} gün)."
                });
            }

            // Hangi kalemler hâlâ iade edilebilir?
            // ⚠️ Kalem kalem bakılıyor: birini iade eden müşteri
            // diğerlerini de iade edebilmeli.
            // ⚠️ Reddedilen talep engel değil, slot boşalıyor.
            var mesgulDurumlar = IadeDurumu.Acikkalanlar
                .Append(IadeDurumu.ParaIadeEdildi)
                .ToArray();

            var mesgulTalepler = await _context.ReturnRequests
                .Where(r => r.OrderId == orderId && mesgulDurumlar.Contains(r.Durum))
                .Select(r => new { r.OrderItemId })
                .ToListAsync();

            // Tüm sipariş iadesi: siparişin HİÇBİR parçası için
            // devam eden/ödenmiş talep olmamalı. Bir kalem zaten
            // iade edilmişse "tümünü iade et" o kalemi ikinci kez
            // iade etmeye çalışırdı.
            var tumSiparisUygun = mesgulTalepler.Count == 0;

            var mesgulKalemIdler = mesgulTalepler
                .Where(x => x.OrderItemId.HasValue)
                .Select(x => x.OrderItemId!.Value)
                .ToHashSet();

            // ⚠️ Sipariş TAMAMI için açık bir talep varsa (OrderItemId
            // null) hiçbir kalem ayrıca iade edilemez — o talep zaten
            // hepsini kapsıyor.
            var tumSiparisTalebiVar = mesgulTalepler.Any(x => x.OrderItemId == null);

            var kalemler = await _context.OrderItems
                .Where(oi => oi.OrderId == orderId)
                .Select(oi => new
                {
                    orderItemId = oi.Id,
                    urunAdi = oi.ProductName,   // dondurulmuş ad
                    adet = oi.Quantity,
                    birimFiyat = oi.UnitPrice
                })
                .ToListAsync();

            var uygunKalemler = tumSiparisTalebiVar
                ? new List<object>()
                : kalemler
                    .Where(k => !mesgulKalemIdler.Contains(k.orderItemId))
                    .Select(k => (object)new
                    {
                        k.orderItemId,
                        k.urunAdi,
                        k.adet,
                        // İade edilirse ne kadar geri gelecek —
                        // hesap tek yerde (IadeHesaplayici).
                        tutar = _hesap.Hesapla(
                            siparis,
                            new OrderItem
                            {
                                UnitPrice = k.birimFiyat,
                                Quantity = k.adet
                            })
                    })
                    .ToList();

            if (!tumSiparisUygun && uygunKalemler.Count == 0)
            {
                return Ok(new
                {
                    uygunMu = false,
                    sebep = "Bu siparişin iade edilebilecek ürünü kalmadı."
                });
            }

            return Ok(new
            {
                uygunMu = true,
                sonGun,
                kalanGun = (int)Math.Ceiling((sonGun - DateTime.UtcNow).TotalDays),

                // Tüm sipariş seçeneği çizilsin mi?
                tumSiparisUygun,

                // Tüm sipariş iade edilirse ödenecek tutar.
                tumSiparisTutari = _hesap.Hesapla(siparis, null),

                kalemler = uygunKalemler
            });
        }
    }
}
