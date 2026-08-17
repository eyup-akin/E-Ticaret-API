using ETicaretAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace ETicaretAPI.Services
{
    // ⭐ YENİ — süresi geçmiş ödenmemiş siparişleri iptal eder.
    //
    // ⚠️ Bu iş olmasa "sipariş önce oluşsun" kararının bedeli ödenmez:
    // müşteri 3DS ekranını kapattığı her seferde stok kalıcı olarak
    // rezerve kalırdı.
    public class OdemeSuresiServisi
    {
        private readonly AppDbContext _context;
        private readonly OdenmemisSiparisTemizleyici _temizleyici;
        private readonly OdemeAyarlari _ayarlar;
        private readonly ILogger<OdemeSuresiServisi> _log;

        public OdemeSuresiServisi(
            AppDbContext context,
            OdenmemisSiparisTemizleyici temizleyici,
            OdemeAyarlari ayarlar,
            ILogger<OdemeSuresiServisi> log)
        {
            _context = context;
            _temizleyici = temizleyici;
            _ayarlar = ayarlar;
            _log = log;
        }

        public async Task SuresiGecenleriIptalEtAsync()
        {
            var sinir = DateTime.UtcNow.AddMinutes(-_ayarlar.BeklemeSuresiDk);

            var adaylar = await _context.Orders
                .Where(o => o.Status == SiparisDurumlari.OdemeBekliyor
                         && o.CreatedAt < sinir

                         // ⚠️ İNCELEMEDE OLANLAR ATLANIYOR. Onlarda para
                         // çekilmiş, iyzico fraud kontrolü sürüyor.
                         // İptal etmek müşterinin parasını almış olup
                         // siparişi silmek demekti.
                         && o.PaymentStatus != OdemeDurumlari.Incelemede)
                .Select(o => o.Id)

                // Bir turda sınırsız iptal uzun kilit tutar; iş bir
                // sonraki turda kaldığı yerden devam ediyor.
                .Take(200)
                .ToListAsync();

            if (adaylar.Count == 0)
            {
                return;
            }

            foreach (var siparisId in adaylar)
            {
                try
                {
                    await _temizleyici.IptalEtAsync(siparisId,
                        $"Ödeme {_ayarlar.BeklemeSuresiDk} dakika içinde tamamlanmadı.");
                }
                catch (Exception ex)
                {
                    // ⚠️ Bir siparişteki hata turu bitirmesin; kalanlar
                    // yine iptal edilsin.
                    _log.LogError(ex,
                        "Süre aşımı iptali başarısız. siparisId: {Id}", siparisId);
                }
            }

            _log.LogInformation(
                "Süre aşımı taraması: {Sayi} sipariş iptal edildi.", adaylar.Count);
        }
    }
}
