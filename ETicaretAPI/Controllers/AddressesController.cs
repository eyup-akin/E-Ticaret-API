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

            var addresses = await _context.Addresses
                .Where(a => a.UserId == userId)
                .Select(a => new AddressDto
                {
                    Id = a.Id,
                    Title = a.Title,
                    FullAddress = a.FullAddress,
                    City = a.City
                })
                .ToListAsync();

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

            var address = new Address
            {
                UserId = userId,
                Title = dto.Title,
                FullAddress = dto.FullAddress,
                City = dto.City
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

            address.Title = dto.Title;
            address.FullAddress = dto.FullAddress;
            address.City = dto.City;

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
    }
}