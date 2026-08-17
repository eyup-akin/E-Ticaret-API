using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ETicaretAPI.Data;
using ETicaretAPI.DTOs;
using ETicaretAPI.Services;

namespace ETicaretAPI.Controllers
{
    // ⭐ DEĞİŞTİ — KART EKLEME VE DÜZENLEME KALDIRILDI.
    //
    // Kart bilgisi artık iyzico'nun ödeme sayfasında toplanıyor ve
    // müşteri orada "kartımı kaydet" derse jeton bize dönüyor
    // (OdemeSonucIsleyici.KartiSaklaAsync). Kart numarası API'mize hiç
    // uğramıyor.
    //
    // ⚠️ CardCreateDto ile birlikte silindi: duran bir alan bir gün
    // doldurulur. Eski kayıtlar (IyzicoCardToken = null) listede
    // görünüyor ama ödemeye kullanılamıyor — "odemeyeHazir" bunu söylüyor.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class CardsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IOdemeSaglayici _saglayici;
        private readonly ILogger<CardsController> _log;

        public CardsController(
            AppDbContext context,
            IOdemeSaglayici saglayici,
            ILogger<CardsController> log)
        {
            _context = context;
            _saglayici = saglayici;
            _log = log;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
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
                    ExpiryYear = c.ExpiryYear,
                    BankaAdi = c.BankaAdi,

                    // ⚠️ Jetonu DIŞARI VERMİYORUZ, yalnızca var mı yok mu.
                    // Jeton ödeme yetkisi taşıyor.
                    OdemeyeHazir = c.IyzicoCardToken != null
                })
                .ToListAsync();

            return Ok(cards);
        }

        // 🟡 DELETE /api/cards/5 — kart sil
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCard(int id)
        {
            var userId = GetUserId();

            // Sahiplik sorguya dahil.
            var card = await _context.Cards
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

            if (card == null)
            {
                return NotFound(new { mesaj = "Kart bulunamadı!" });
            }

            var cardUserKey = await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => u.IyzicoCardUserKey)
                .FirstOrDefaultAsync();

            // ⚠️ Önce sağlayıcıdan sil. Ters sırada yapsak yerel kayıt
            // gider, jeton iyzico'da kalır ve bir daha ulaşamayız.
            if (card.IyzicoCardToken != null && cardUserKey != null)
            {
                var silindi = await _saglayici.KartSilAsync(cardUserKey, card.IyzicoCardToken);

                if (!silindi)
                {
                    _log.LogWarning(
                        "Kart iyzico'dan silinemedi, yerel kayıt korunuyor. kartId: {Id}", id);

                    return StatusCode(502, new
                    {
                        mesaj = "Kart şu an silinemedi, biraz sonra tekrar dener misin?"
                    });
                }
            }

            _context.Cards.Remove(card);
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Kart silindi!" });
        }
    }
}
