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
    // ============================================================
    //  ⭐ YENİ (Aşama 8) — DESTEK TALEPLERİ (ADMİN TARAFI)
    //
    //  ⚠️ Müşteri uçları `SupportController`'da. Ayrımın gerekçesi
    //  orada yazılı: `OrdersController`'ın iki tarafı bir arada
    //  tutup 900 satıra ulaşması Aşama 11'in refactor listesine
    //  girmişti; aynı hata yeni dosyada tekrarlanmadı.
    //
    //  ⚠️ SAHİPLİK KONTROLÜ YOK — bilinçli. Admin tüm talepleri
    //  görür, zaten işi bu. Yetki kapısı controller seviyesindeki
    //  [Authorize(Roles = "admin")]: üç katmanlı yetkinin
    //  ÜÇÜNCÜSÜ, yani gerçek olan.
    // ============================================================
    [Route("api/admin/destek")]
    [ApiController]
    [Authorize(Roles = "admin")]
    public class AdminSupportController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly DestekYazismasi _yazisma;

        public AdminSupportController(AppDbContext context, DestekYazismasi yazisma)
        {
            _context = context;
            _yazisma = yazisma;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // 🔴 GET /api/admin/destek?durum=acik&kategori=kargo&sayfa=1
        [HttpGet]
        public async Task<IActionResult> Liste(
            [FromQuery] string? durum,
            [FromQuery] string? kategori,
            [FromQuery] string? search,
            [FromQuery] int sayfa = 1,
            [FromQuery] int sayfaBoyutu = 20)
        {
            if (sayfa < 1) sayfa = 1;
            if (sayfaBoyutu < 1 || sayfaBoyutu > 100) sayfaBoyutu = 20;

            var query = _context.SupportTickets.AsQueryable();

            // ⚠️ Tanımadığımız filtre değeri SESSİZCE ATLANIYOR,
            // hata dönmüyor: filtre parametresi adminin listeyi
            // görmesini engelleyen bir şey olmamalı. (Ürün
            // filtrelerinde verilen kararın aynısı.)
            if (!string.IsNullOrWhiteSpace(durum))
            {
                query = query.Where(t => t.Durum == durum);
            }

            if (!string.IsNullOrWhiteSpace(kategori))
            {
                query = query.Where(t => t.Kategori == kategori);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var arama = search.Trim();
                query = query.Where(t => t.Konu.Contains(arama));
            }

            var toplam = await query.CountAsync();

            // ⚠️ Sıralama SON HAREKETE göre: adminin sorusu "en son
            // ne konuşuldu", "ne zaman açıldı" değil.
            // ⚠️ İkincil ölçüt (Id) şart: aynı `UpdatedAt` değerine
            // sahip iki talep varsa sayfalamada biri iki sayfada
            // birden görünebilirdi.
            var talepler = await query
                .OrderByDescending(t => t.UpdatedAt)
                .ThenByDescending(t => t.Id)
                .Skip((sayfa - 1) * sayfaBoyutu)
                .Take(sayfaBoyutu)
                .Select(t => new TalepOzetDto
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
                    UpdatedAt = t.UpdatedAt,
                    MesajSayisi = _context.SupportMessages.Count(m => m.TicketId == t.Id),

                    // ⚠️ Müşteri adı SADECE admin tarafında: listede
                    // "kim yazmış" olmadan talepler birbirinden
                    // ayrılmıyor.
                    MusteriAdi = _context.Users
                        .Where(u => u.Id == t.UserId)
                        .Select(u => u.FullName)
                        .FirstOrDefault()
                })
                .ToListAsync();

            // Sekme rozetleri için durum sayıları.
            //
            // ⚠️ SAYILAR SUNUCUDAN, istemcide listeyi süzerek DEĞİL.
            // Liste sayfalı; istemcide saysaydık yalnızca ilk sayfayı
            // sayardık ve rakam sessizce yanlış çıkardı. (Aynı ders
            // mobil sipariş durum özetinde alınmıştı.)
            //
            // ⚠️ Sayım FİLTRESİZ `SupportTickets` üzerinden: sekme
            // rozeti "bu durumda toplam kaç talep var" demeli, "şu
            // anki filtreye göre kaç" değil — yoksa "açık" sekmesine
            // basınca diğer sekmelerin sayısı sıfırlanırdı.
            var sayilar = await _context.SupportTickets
                .GroupBy(t => t.Durum)
                .Select(g => new { durum = g.Key, adet = g.Count() })
                .ToListAsync();

            int Say(string d) => sayilar.FirstOrDefault(x => x.durum == d)?.adet ?? 0;

            return Ok(new
            {
                talepler,
                toplam,
                sayfa,
                sayfaBoyutu,
                toplamSayfa = (int)Math.Ceiling(toplam / (double)sayfaBoyutu),

                durumSayilari = new
                {
                    acik = Say(DestekDurumu.Acik),
                    yanitlandi = Say(DestekDurumu.Yanitlandi),
                    kapali = Say(DestekDurumu.Kapali)
                }
            });
        }

        // 🔴 GET /api/admin/destek/5
        [HttpGet("{id}")]
        public async Task<IActionResult> Detay(int id)
        {
            var talep = await _context.SupportTickets
                .Where(t => t.Id == id)
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
                    UpdatedAt = t.UpdatedAt,

                    MusteriAdi = _context.Users
                        .Where(u => u.Id == t.UserId)
                        .Select(u => u.FullName)
                        .FirstOrDefault(),

                    // ⚠️ E-posta admin tarafında GÖSTERİLİYOR: destek
                    // işinin doğası gereği adminin müşteriye ulaşması
                    // gerekebiliyor. Kart ve adres bilgisinden farkı
                    // bu — onlar panele hiç taşınmıyor (karar #11),
                    // e-posta ise zaten müşteri detayında da var.
                    MusteriEposta = _context.Users
                        .Where(u => u.Id == t.UserId)
                        .Select(u => u.Email)
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync();

            if (talep == null)
            {
                return NotFound(new { mesaj = "Talep bulunamadı!" });
            }

            talep.Mesajlar = await _yazisma.MesajlariGetirAsync(id);

            return Ok(talep);
        }

        // 🔴 POST /api/admin/destek/5/cevap
        [HttpPost("{id}/cevap")]
        public async Task<IActionResult> Cevapla(int id, [FromBody] MesajEkleDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var talep = await _context.SupportTickets.FirstOrDefaultAsync(t => t.Id == id);

            if (talep == null)
            {
                return NotFound(new { mesaj = "Talep bulunamadı!" });
            }

            var simdi = DateTime.UtcNow;

            _context.SupportMessages.Add(new SupportMessage
            {
                TicketId = talep.Id,
                GonderenUserId = GetUserId(),

                // ⚠️ `true` SABİT olarak yazılıyor, rolden okunmuyor.
                // Bu alan "o an hangi sıfatla konuşuldu" bilgisinin
                // donmuş hâli; gerekçesi modelde yazılı.
                GonderenAdminMi = true,

                Mesaj = dto.Mesaj.Trim(),
                CreatedAt = simdi
            });

            // ⚠️ KAPALI TALEBE CEVAP YAZMAK ONU YENİDEN AÇMIYOR,
            // "yanıtlandı" yapıyor: admin kapanmış bir konuya ek bilgi
            // düşmüş olabilir. Müşteri tarafındaki kural farklı ve
            // bilinçli — orada yazan kişi hâlâ çözüm bekliyor demektir.
            talep.Durum = DestekDurumu.Yanitlandi;
            talep.UpdatedAt = simdi;

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Cevabın gönderildi." });
        }

        // 🔴 PUT /api/admin/destek/5/durum
        //
        // ⚠️ NEDEN DURUM MAKİNESİ (GecerliGecisler) YOK — oysa
        // siparişte var?
        //
        // Sipariş durumları geri alınamaz yan etkiler taşıyor: stok
        // düşüyor, para iade ediliyor, kargoya veriliyor. Bu yüzden
        // "kargoda"dan "hazirlaniyor"a dönmek yasak.
        //
        // Destek durumlarının hiçbirinin yan etkisi yok ve üçü de
        // birbirinden erişilebilir olmalı: yanlışlıkla kapatılan bir
        // talep tekrar açılabilmeli. Olmayan bir kısıt uydurmak,
        // adminin elini bağlayıp destek verememesine yol açardı.
        //
        // Kalan tek risk yazım hatası; onu beyaz liste kapatıyor.
        [HttpPut("{id}/durum")]
        public async Task<IActionResult> DurumDegistir(int id, [FromBody] TalepDurumDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // ⚠️ BEYAZ LİSTE SUNUCUDA — panelin üç seçenek göstermesi
            // yetmez, istek Postman'den de gelebilir.
            var gecerliler = new[]
            {
                DestekDurumu.Acik, DestekDurumu.Yanitlandi, DestekDurumu.Kapali
            };

            if (!gecerliler.Contains(dto.Durum))
            {
                return BadRequest(new { mesaj = "Geçersiz durum!" });
            }

            var talep = await _context.SupportTickets.FirstOrDefaultAsync(t => t.Id == id);

            if (talep == null)
            {
                return NotFound(new { mesaj = "Talep bulunamadı!" });
            }

            talep.Durum = dto.Durum;
            talep.UpdatedAt = DateTime.UtcNow;

            // ⚠️ Kapatan kaydı yalnızca KAPALI durumda anlamlı;
            // açılan talepte eski kapatanı bırakmak "bu talebi X
            // kapattı ama açık" gibi çelişkili bir kayıt üretirdi.
            talep.KapatanUserId = dto.Durum == DestekDurumu.Kapali
                ? GetUserId()
                : null;

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Talep durumu güncellendi." });
        }
    }
}
