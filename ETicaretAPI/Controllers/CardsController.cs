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
    [Authorize] // tüm kart işlemleri giriş ister
    public class CardsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CardsController(AppDbContext context)
        {
            _context = context;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        // Kart numarasının ilk hanesinden tipini tahmin eder (basit)
        private string KartTipiBul(string cardNumber)
        {
            if (cardNumber.StartsWith("4")) return "Visa";
            if (cardNumber.StartsWith("5")) return "Mastercard";
            if (cardNumber.StartsWith("3")) return "Amex";
            return "Bilinmeyen";
        }

        // 🟡 GET /api/cards — kartlarım
        [HttpGet]
        public async Task<IActionResult> GetCards()
        {
            var userId = GetUserId();

            var cards = await _context.Cards
                .Where(c => c.UserId == userId)
                .Select(c => new CardDto
                {
                    Id = c.Id,
                    CardHolderName = c.CardHolderName,
                    Last4Digits = c.Last4Digits,
                    CardType = c.CardType,
                    ExpiryMonth = c.ExpiryMonth,
                    ExpiryYear = c.ExpiryYear
                })
                .ToListAsync();

            return Ok(cards);
        }

        // 🟡 POST /api/cards — kart ekle
        [HttpPost]
        public async Task<IActionResult> AddCard([FromBody] CardCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Son kullanma tarihi geçmiş mi? (basit kontrol)
            var now = DateTime.UtcNow;
            if (dto.ExpiryYear < now.Year ||
                (dto.ExpiryYear == now.Year && dto.ExpiryMonth < now.Month))
            {
                return BadRequest(new { mesaj = "Kartın son kullanma tarihi geçmiş biladerim!" });
            }

            var userId = GetUserId();

            // İŞTE KRİTİK KISIM: tam numaradan SADECE son 4 haneyi al, gerisini SAKLAMA
            var card = new Card
            {
                UserId = userId,
                CardHolderName = dto.CardHolderName,
                Last4Digits = dto.CardNumber.Substring(dto.CardNumber.Length - 4), // son 4 hane
                CardType = KartTipiBul(dto.CardNumber),
                ExpiryMonth = dto.ExpiryMonth,
                ExpiryYear = dto.ExpiryYear
            };
            // dto.CardNumber (tam) ve dto.Cvv hiçbir yere kaydedilmiyor — metod bitince yok oluyorlar

            _context.Cards.Add(card);
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Kart eklendi biladerim!", last4 = card.Last4Digits });
        }

        // 🟡 PUT /api/cards/5 — kart düzenle (sadece isim ve tarih; numara değiştirilemez)
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCard(int id, [FromBody] CardCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetUserId();

            var card = await _context.Cards
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

            if (card == null)
            {
                return NotFound(new { mesaj = "Kart bulunamadı!" });
            }

            // Numara değişirse son 4 haneyi güncelle
            card.CardHolderName = dto.CardHolderName;
            card.Last4Digits = dto.CardNumber.Substring(dto.CardNumber.Length - 4);
            card.CardType = KartTipiBul(dto.CardNumber);
            card.ExpiryMonth = dto.ExpiryMonth;
            card.ExpiryYear = dto.ExpiryYear;

            await _context.SaveChangesAsync();
            return Ok(new { mesaj = "Kart güncellendi!" });
        }

        // 🟡 DELETE /api/cards/5 — kart sil
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCard(int id)
        {
            var userId = GetUserId();

            var card = await _context.Cards
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

            if (card == null)
            {
                return NotFound(new { mesaj = "Kart bulunamadı!" });
            }

            _context.Cards.Remove(card);
            await _context.SaveChangesAsync();
            return Ok(new { mesaj = "Kart silindi!" });
        }
    }
}