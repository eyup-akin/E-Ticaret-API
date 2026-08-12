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
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AddressesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AddressesController(AppDbContext context)
        {
            _context = context;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // 🟡 GET /api/addresses — adreslerim
        [HttpGet]
        public async Task<IActionResult> GetAddresses()
        {
            var userId = GetUserId();

            // ⭐ DEĞİŞTİ (4.9) — telefon artık kolonda değil, JOIN'de.
            //
            // ⚠️ LEFT JOIN (DefaultIfEmpty), INNER DEĞİL. Numarası
            // silinmiş bir adres INNER JOIN'de listeden komple
            // kaybolurdu — müşteri adresinin silindiğini sanardı.
            // "INNER JOIN satır düşürür" dersi OrderItem'da alınmıştı.
            var satirlar = await (
                from a in _context.Addresses
                where a.UserId == userId
                join p in _context.Phones on a.PhoneId equals p.Id into pg
                from p in pg.DefaultIfEmpty()
                select new { a.Id, a.Title, a.FullAddress, a.City, a.PhoneId, p!.Numara }
            ).ToListAsync();

            // Gorunum biçimi C# tarafında üretiliyor (SQL'e çevrilemez).
            var addresses = satirlar.Select(x => new AddressDto
            {
                Id = x.Id,
                Title = x.Title,
                FullAddress = x.FullAddress,
                City = x.City,
                PhoneId = x.PhoneId,
                Phone = x.Numara == null ? null : TelefonBicimi.Goster(x.Numara)
            }).ToList();

            return Ok(addresses);
        }

        // 🟡 POST /api/addresses — adres ekle
        [HttpPost]
        public async Task<IActionResult> AddAddress([FromBody] AddressCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetUserId();

            // ⭐ YENİ (4.9) — seçilen numara gerçekten bu kullanıcının mı?
            //
            // ⚠️ Kontrol SORGUNUN İÇİNDE (`&& p.UserId == userId`),
            // ayrı bir if olarak değil. Ayrı yazsaydık bir gün
            // birleştirilirken unutulabilirdi ve başkasının
            // numarasını kendi adresine bağlamak mümkün olurdu.
            if (!await TelefonBuKullanicininMi(dto.PhoneId, userId))
            {
                return BadRequest(new { mesaj = "Geçerli bir telefon numarası seçmelisin!" });
            }

            var address = new Address
            {
                UserId = userId,
                Title = dto.Title,
                FullAddress = dto.FullAddress,
                City = dto.City,
                PhoneId = dto.PhoneId        // ⭐ DEĞİŞTİ (4.9)
            };

            _context.Addresses.Add(address);
            await _context.SaveChangesAsync();
            return Ok(new { mesaj = "Adres eklendi biladerim!", id = address.Id });
        }

        // 🟡 PUT /api/addresses/5 — adres düzenle
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateAddress(int id, [FromBody] AddressCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetUserId();

            var address = await _context.Addresses
                .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);

            if (address == null)
            {
                return NotFound(new { mesaj = "Adres bulunamadı!" });
            }

            // ⭐ YENİ (4.9) — eklemedeki kontrolün aynısı
            if (!await TelefonBuKullanicininMi(dto.PhoneId, userId))
            {
                return BadRequest(new { mesaj = "Geçerli bir telefon numarası seçmelisin!" });
            }

            address.Title = dto.Title;
            address.FullAddress = dto.FullAddress;
            address.City = dto.City;
            address.PhoneId = dto.PhoneId;      // ⭐ DEĞİŞTİ (4.9)

            await _context.SaveChangesAsync();
            return Ok(new { mesaj = "Adres güncellendi!" });
        }

        // 🟡 DELETE /api/addresses/5 — adres sil
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAddress(int id)
        {
            var userId = GetUserId();

            var address = await _context.Addresses
                .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);

            if (address == null)
            {
                return NotFound(new { mesaj = "Adres bulunamadı!" });
            }

            _context.Addresses.Remove(address);
            await _context.SaveChangesAsync();
            return Ok(new { mesaj = "Adres silindi!" });
        }

        // ⭐ YENİ (4.9) — ekleme ve düzenlemenin ortak kontrolü.
        //
        // İki tüketicisi olduğu an metoda çıkarıldı; tek yerde
        // kalsaydı orada durmaya devam ederdi.
        private Task<bool> TelefonBuKullanicininMi(int phoneId, int userId)
        {
            return _context.Phones.AnyAsync(p => p.Id == phoneId && p.UserId == userId);
        }
    }
}