using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;

namespace ETicaretAPI.Services
{
    // ⭐ YENİ (Aşama 10) — ONAY KAYDI, TEK YERDE
    //
    // İki tüketici: kayıt (gizlilik + kullanım) ve sipariş (mesafeli
    // satış + ön bilgilendirme). Ayrı ayrı yazsalardı biri aktif
    // sürümü bulmayı, diğeri IP'yi kaydetmeyi unutabilirdi.
    public class SozlesmeOnayServisi
    {
        private readonly AppDbContext _context;

        public SozlesmeOnayServisi(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Verilen tiplerin AKTİF sürümlerine onay kaydı ekler.
        /// ⚠️ SaveChanges ÇAĞIRMAZ — çağıranın transaction'ıyla aynı
        /// anda yazılsın diye (StokDefteri'ndeki desen).
        /// </summary>
        public async Task EkleAsync(int userId, string[] tipler, string? ip, int? orderId = null)
        {
            var aktifler = await _context.Sozlesmeler
                .Where(s => s.AktifMi && tipler.Contains(s.Tip))
                .Select(s => s.Id)
                .ToListAsync();

            var simdi = DateTime.UtcNow;

            foreach (var sozlesmeId in aktifler)
            {
                _context.SozlesmeOnaylari.Add(new SozlesmeOnayi
                {
                    UserId = userId,
                    SozlesmeId = sozlesmeId,
                    OnayTarihi = simdi,
                    IpAdresi = ip,
                    OrderId = orderId
                });
            }
        }
    }
}
