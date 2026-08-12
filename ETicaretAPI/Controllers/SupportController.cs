using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;
using ETicaretAPI.Services;

namespace ETicaretAPI.Controllers
{
    // ============================================================
    //  ⭐ YENİ (Aşama 8) — DESTEK TALEPLERİ (MÜŞTERİ TARAFI)
    //
    //  ⚠️ ADMİN UÇLARI BURADA DEĞİL — `AdminSupportController`'da.
    //
    //  `OrdersController` müşteri ve admin uçlarını bir arada
    //  tutarak ~900 satıra ulaştı ve Aşama 11'in refactor listesine
    //  "admin bölümü ayrı controller'a" diye yazıldı. Aynı hatayı
    //  yeni bir dosyada tekrarlamanın anlamı yok: ayrım BAŞTAN
    //  yapılıyor.
    //
    //  Ayrımın ikinci faydası yetki: bu dosyadaki her uç müşteriye
    //  ait ve sahiplik kontrolü şart; oradaki her uç admin'e ait ve
    //  sahiplik diye bir şey yok. İkisi bir aradayken hangi ucun
    //  hangi kurala tabi olduğu okuyanın dikkatine kalıyor.
    // ============================================================
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SupportController : ControllerBase
    {
        private readonly AppDbContext _context;

        // ⚠️ Yazışma okuma servisi: aynı sorgu admin tarafında da
        // kullanılıyor, iki kopya olmasın diye ortak yerde.
        private readonly DestekYazismasi _yazisma;

        public SupportController(AppDbContext context, DestekYazismasi yazisma)
        {
            _context = context;
            _yazisma = yazisma;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // 🟡 POST /api/support — yeni talep aç
        //
        // ⚠️ Rate limit: talep açmak spam'e açık bir yazma işlemi.
        // Gerekçenin tamamı Program.cs'teki "destek" politikasında.
        [HttpPost]
        [EnableRateLimiting("destek")]
        public async Task<IActionResult> TalepAc([FromBody] TalepOlusturDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetUserId();

            // ⚠️ BEYAZ LİSTE SUNUCUDA. Mobilin dört seçenekli bir menü
            // göstermesi yetmez — istek Postman'den de gelebilir ve
            // "kategori=<script>" yazılabilir.
            if (!DestekKategorisi.Gecerliler.Contains(dto.Kategori))
            {
                return BadRequest(new { mesaj = "Geçersiz kategori!" });
            }

            // ⚠️ SİPARİŞ SAHİPLİĞİ SORGUYA GİRİYOR.
            // Başkasının sipariş numarasını talebine bağlayabilseydi,
            // admin ekranında o siparişin bilgileri yabancı birine
            // gösterilirdi — dolaylı bir IDOR.
            if (dto.OrderId.HasValue)
            {
                var siparisVarMi = await _context.Orders
                    .AnyAsync(o => o.Id == dto.OrderId.Value && o.UserId == userId);

                if (!siparisVarMi)
                {
                    return BadRequest(new { mesaj = "Geçerli bir sipariş seçmelisin!" });
                }
            }

            var simdi = DateTime.UtcNow;

            var talep = new SupportTicket
            {
                UserId = userId,
                OrderId = dto.OrderId,
                Konu = dto.Konu.Trim(),
                Kategori = dto.Kategori,
                Durum = DestekDurumu.Acik,
                CreatedAt = simdi,
                UpdatedAt = simdi
            };

            // ⚠️ TALEP VE İLK MESAJ TEK TRANSACTION'DA.
            //
            // Araya bir hata girerse mesajsız bir talep kalırdı:
            // admin ekranında konusu olan ama içi boş bir kayıt.
            // İkisi ayrılamaz — talep zaten "birinin bir sorusu var"
            // demek ve soru mesajın içinde.
            await using var tx = await _context.Database.BeginTransactionAsync();

            _context.SupportTickets.Add(talep);
            await _context.SaveChangesAsync();   // Id lazım

            _context.SupportMessages.Add(new SupportMessage
            {
                TicketId = talep.Id,
                GonderenUserId = userId,
                GonderenAdminMi = false,
                Mesaj = dto.Mesaj.Trim(),
                CreatedAt = simdi
            });

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new { mesaj = "Talebin oluşturuldu.", id = talep.Id });
        }

        // 🟡 GET /api/support — taleplerim
        [HttpGet]
        public async Task<IActionResult> Taleplerim()
        {
            var userId = GetUserId();

            // ⚠️ Sıralama `UpdatedAt`'e göre: müşteri de en son
            // konuşulan talebi üstte görmeli. `CreatedAt` olsaydı
            // dün cevap gelen eski bir talep listenin dibinde kalırdı.
            var talepler = await _context.SupportTickets
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.UpdatedAt)
                .ThenByDescending(t => t.Id)   // eşitlikte kararlı sıra
                .Select(t => new TalepOzetDto
                {
                    Id = t.Id,
                    Konu = t.Konu,
                    Kategori = t.Kategori,
                    Durum = t.Durum,
                    OrderId = t.OrderId,

                    // Elle birleştirme: Order'da gezinme özelliği yok.
                    // EF bunu tek SQL'e (LEFT JOIN) çeviriyor.
                    SiparisNo = _context.Orders
                        .Where(o => o.Id == t.OrderId)
                        .Select(o => o.OrderNumber)
                        .FirstOrDefault(),

                    CreatedAt = t.CreatedAt,
                    UpdatedAt = t.UpdatedAt,
                    MesajSayisi = _context.SupportMessages.Count(m => m.TicketId == t.Id)

                    // ⚠️ MusteriAdi doldurulmuyor: müşteri kendi adını
                    // zaten biliyor. Admin listesinde dolduruluyor.
                })
                .ToListAsync();

            return Ok(talepler);
        }

        // 🟡 GET /api/support/5 — talep detayı (yazışmayla)
        [HttpGet("{id}")]
        public async Task<IActionResult> TalepDetay(int id)
        {
            var userId = GetUserId();

            // ⚠️ SAHİPLİK KONTROLÜ SORGUYA GİRDİ, ayrı bir if olarak
            // değil — ayrı if unutulabilir.
            //
            // ⚠️ Yetkisizde 404, 403 DEĞİL: "bu senin değil" demek
            // kaydın var olduğunu sızdırır. Saldırgan id'leri tarayıp
            // hangi numaraların gerçek talep olduğunu öğrenirdi.
            var talep = await _context.SupportTickets
                .Where(t => t.Id == id && t.UserId == userId)
                .Select(t => new TalepDetayDto
                {
                    Id = t.Id,
                    Konu = t.Konu,
                    Kategori = t.Kategori,
                    Durum = t.Durum,
                    OrderId = t.OrderId,
                    SiparisNo = _context.Orders
                        .Where(o => o.Id == t.OrderId)
                        .Select(o => o.OrderNumber)
                        .FirstOrDefault(),
                    CreatedAt = t.CreatedAt,
                    UpdatedAt = t.UpdatedAt
                })
                .FirstOrDefaultAsync();

            if (talep == null)
            {
                return NotFound(new { mesaj = "Talep bulunamadı!" });
            }

            talep.Mesajlar = await _yazisma.MesajlariGetirAsync(id);

            return Ok(talep);
        }

        // 🟡 POST /api/support/5/mesaj — yazışmaya devam et
        [HttpPost("{id}/mesaj")]
        public async Task<IActionResult> MesajEkle(int id, [FromBody] MesajEkleDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetUserId();

            var talep = await _context.SupportTickets
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (talep == null)
            {
                return NotFound(new { mesaj = "Talep bulunamadı!" });
            }

            var simdi = DateTime.UtcNow;

            _context.SupportMessages.Add(new SupportMessage
            {
                TicketId = talep.Id,
                GonderenUserId = userId,
                GonderenAdminMi = false,
                Mesaj = dto.Mesaj.Trim(),
                CreatedAt = simdi
            });

            // ⚠️ MÜŞTERİ YAZDIYSA TOP YİNE BİZDE: durum "açık"a
            // dönüyor. Kapalı bir talebe yazmak onu YENİDEN AÇIYOR.
            //
            // Alternatif "kapalıya yazılamaz, yeni talep aç"tı ve
            // elendi: müşterinin tekrar yazma sebebi genelde sorunun
            // ÇÖZÜLMEMİŞ olması. Yeni talebe zorlamak aynı konuşmayı
            // iki kayda bölerdi ve admin ikincisini bağlamsız
            // okurdu — üstelik "kapattım, kurtuldum" diye bir
            // davranışı ödüllendirirdi.
            talep.Durum = DestekDurumu.Acik;
            talep.UpdatedAt = simdi;

            // Yeniden açılan talepte eski kapatan bilgisi yanıltıcı
            // olurdu: kayıt "kapalı" değil artık.
            talep.KapatanUserId = null;

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Mesajın gönderildi." });
        }

        // 🟡 PUT /api/support/5/kapat — müşteri kendi talebini kapatır
        //
        // ⚠️ NİYET ADRESTE YAZILI: tek uca `durum=...` göndermek
        // yerine ayrı bir uç. Müşterinin yapabileceği tek durum
        // değişikliği bu; ona bütün durum makinesini açmak, yarın
        // "yanitlandi" yazıp talebi cevaplanmış göstermesine izin
        // vermek olurdu.
        //
        // ⚠️ PUT ve idempotent: zaten kapalı bir talebi kapatmak hata
        // değil, aynı sonuç.
        [HttpPut("{id}/kapat")]
        public async Task<IActionResult> Kapat(int id)
        {
            var userId = GetUserId();

            var talep = await _context.SupportTickets
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (talep == null)
            {
                return NotFound(new { mesaj = "Talep bulunamadı!" });
            }

            talep.Durum = DestekDurumu.Kapali;
            talep.KapatanUserId = userId;
            talep.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Talep kapatıldı." });
        }

    }
}
