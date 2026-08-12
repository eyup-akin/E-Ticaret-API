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
    // ⭐ YENİ (4.9) — TELEFON DEFTERİ
    //
    // Adres ve kart yönetiminin deseni birebir aynı: müşterinin
    // kendi kayıtları, sahiplik kontrolü sorgunun WHERE'inde.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PhonesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public PhonesController(AppDbContext context)
        {
            _context = context;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // 🟡 GET /api/phones — numaralarım
        [HttpGet]
        public async Task<IActionResult> GetPhones()
        {
            var userId = GetUserId();

            var satirlar = await _context.Phones
                .Where(p => p.UserId == userId)
                // Varsayılan hep başta; gerisi eskiden yeniye.
                // ⚠️ Sıralama sunucuda çünkü "hangisi asıl numara"
                // bir veri gerçeği, ekran tercihi değil — üç
                // tüketicinin (mobil defter, mobil adres formu,
                // admin) aynı sırayı görmesi gerekiyor.
                .OrderByDescending(p => p.VarsayilanMi)
                .ThenBy(p => p.Id)
                .ToListAsync();

            // ⚠️ Gorunum'u SQL'de üretemeyiz (TelefonBicimi C# kodu),
            // bu yüzden önce ToListAsync sonra Select. Liste bir
            // müşterinin numaraları — birkaç satır, bellekte
            // dönmenin maliyeti yok.
            var dtolar = satirlar.Select(DtoyaCevir).ToList();

            return Ok(dtolar);
        }

        // 🟡 POST /api/phones — numara ekle
        [HttpPost]
        public async Task<IActionResult> AddPhone([FromBody] PhoneCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetUserId();

            // ⚠️ NORMALİZASYON KAYDETMEDEN ÖNCE. "0532 123 45 67" ile
            // "+905321234567" aynı numara; ham haliyle saklasaydık
            // benzersizlik indeksi ikisini ayrı sanardı.
            var numara = TelefonBicimi.Normalize(dto.Numara);
            if (numara == null)
            {
                return BadRequest(new
                {
                    mesaj = "Numarayı okuyamadım. Alan koduyla birlikte 10 hane " +
                            "olmalı (örn: 0532 123 45 67)."
                });
            }

            // İlk numara otomatik varsayılan olsun: müşteri tek
            // numarası varken ayrıca "bunu varsayılan yap" demek
            // zorunda kalmasın. Aynı işi yapan ikinci bir adım,
            // adım değil engeldir.
            var ilkNumaraMi = !await _context.Phones.AnyAsync(p => p.UserId == userId);

            var kayit = new Phone
            {
                UserId = userId,
                Numara = numara,
                Etiket = dto.Etiket.Trim(),
                DogrulandiMi = false,   // ⚠️ SMS doğrulaması Faz 2
                VarsayilanMi = ilkNumaraMi,
                CreatedAt = DateTime.UtcNow
            };

            _context.Phones.Add(kayit);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // ⚠️ Önce "var mı?" diye sormuyoruz, doğrudan yazıp
                // ihlali yakalıyoruz. Sorsaydık iki eşzamanlı istek
                // arasında yarış kalırdı; indeks zaten kesin cevabı
                // veriyor. (StockAlert ve AdminBasvuru'daki desen.)
                return Conflict(new { mesaj = "Bu numara zaten kayıtlı." });
            }

            return Ok(new { mesaj = "Numara eklendi.", id = kayit.Id });
        }

        // 🟡 PUT /api/phones/5/varsayilan — asıl numara yap
        //
        // ⚠️ NİYET ADRESTE YAZILI. Tek bir uca `varsayilan=true/false`
        // göndermek yerine ayrı bir uç: "varsayılanı kaldır" diye bir
        // işlem YOK zaten — her zaman tam bir tane varsayılan olur.
        //
        // ⚠️ PUT ve idempotent: zaten varsayılan olan numaraya ikinci
        // kez basmak hata değil, aynı sonuç.
        [HttpPut("{id}/varsayilan")]
        public async Task<IActionResult> VarsayilanYap(int id)
        {
            var userId = GetUserId();

            // ⚠️ Sahiplik kontrolü SORGUYA girdi, ayrı bir if olarak
            // değil — ayrı if unutulabilir. Başkasının numarasında
            // 404: 403 demek kaydın var olduğunu sızdırırdı.
            var varMi = await _context.Phones
                .AnyAsync(p => p.Id == id && p.UserId == userId);

            if (!varMi)
            {
                return NotFound(new { mesaj = "Numara bulunamadı!" });
            }

            // ⚠️ İKİ UPDATE TEK TRANSACTION'DA.
            // Arada bir hata olsaydı kullanıcı HİÇ varsayılanı olmayan
            // bir defterle kalırdı. Sıra da önemli: önce hepsini
            // kapat, sonra birini aç. Ters sırada, kapatma adımı
            // az önce açtığımızı da kapatırdı.
            await using var tx = await _context.Database.BeginTransactionAsync();

            await _context.Phones
                .Where(p => p.UserId == userId && p.VarsayilanMi)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.VarsayilanMi, false));

            await _context.Phones
                .Where(p => p.Id == id && p.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.VarsayilanMi, true));

            await tx.CommitAsync();

            return Ok(new { mesaj = "Asıl numara güncellendi." });
        }

        // 🟡 DELETE /api/phones/5 — numara sil
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePhone(int id)
        {
            var userId = GetUserId();

            var kayit = await _context.Phones
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

            if (kayit == null)
            {
                return NotFound(new { mesaj = "Numara bulunamadı!" });
            }

            // ⚠️ SİLMEYİ ENGELLEMİYORUZ — adrese bağlı olsa bile.
            // FK'da ON DELETE SET NULL var: bağlı adresler telefonsuz
            // kalıyor ve sipariş akışı yeniden seçim istiyor.
            // Engelleseydik müşteri, kullandığı numarayı değiştirmek
            // için önce tüm adreslerini düzenlemek zorunda kalırdı.
            //
            // ⚠️ Geçmiş siparişler ETKİLENMİYOR: Order.ShippingPhone
            // dondurulmuş bir kopya, bu tabloya bağlı değil.
            var varsayilanMiydi = kayit.VarsayilanMi;

            _context.Phones.Remove(kayit);
            await _context.SaveChangesAsync();

            // Varsayılan silindiyse defter varsayılansız kalmasın:
            // kalan en eski numara devralır.
            //
            // ⚠️ "Kalan yoksa" durumu sessizce geçiliyor — defter boş,
            // devralacak bir şey yok. Uydurma bir kayıt yaratmıyoruz.
            if (varsayilanMiydi)
            {
                var devralan = await _context.Phones
                    .Where(p => p.UserId == userId)
                    .OrderBy(p => p.Id)
                    .FirstOrDefaultAsync();

                if (devralan != null)
                {
                    devralan.VarsayilanMi = true;
                    await _context.SaveChangesAsync();
                }
            }

            return Ok(new { mesaj = "Numara silindi." });
        }

        // ⚠️ Tek yerde kullanılıyor gibi görünüyor ama üç uç da
        // (liste, admin dökümü, adres dökümü) aynı biçimi üretmek
        // zorunda; dönüşümü tek satıra indirmek onu ileride ortak
        // yere taşımayı da kolaylaştırıyor.
        private static PhoneDto DtoyaCevir(Phone p) => new()
        {
            Id = p.Id,
            Numara = p.Numara,
            Gorunum = TelefonBicimi.Goster(p.Numara),
            Etiket = p.Etiket,
            DogrulandiMi = p.DogrulandiMi,
            VarsayilanMi = p.VarsayilanMi,
            CreatedAt = p.CreatedAt
        };
    }
}
