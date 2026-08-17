using ETicaretAPI.Data;
using ETicaretAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ETicaretAPI.Services
{
    public enum IadeYolu
    {
        // Ödeme kaydı yok (bu özellikten önceki siparişler) — sağlayıcıya
        // gidilmedi, yalnızca veritabanı güncellenecek.
        SaglayiciYok,
        Iptal,      // cancel — aynı gün, tam tutar
        Iade        // refund — kalem bazlı
    }

    public record GercekIadeSonucu(
        bool Basarili,
        IadeYolu Yol,
        decimal GonderilenTutar,
        string? HataMesaji);


    // ============================================================
    //  ⭐ YENİ — İADEYİ GERÇEKTEN SAĞLAYICIYA GÖNDERİR.
    //
    //  Bugüne kadar iade yalnızca veritabanına yazılıyordu; para
    //  müşteriye hiç dönmüyordu.
    //
    //  ⚠️ ÖNCE SAĞLAYICI, SONRA VERİTABANI. Ters sırada yazsak iyzico
    //  reddettiğinde veritabanı "iade edildi" der ve para hiç gitmez.
    // ============================================================
    public class IadeGonderici
    {
        private readonly AppDbContext _context;
        private readonly IOdemeSaglayici _saglayici;
        private readonly ILogger<IadeGonderici> _log;

        public IadeGonderici(
            AppDbContext context,
            IOdemeSaglayici saglayici,
            ILogger<IadeGonderici> log)
        {
            _context = context;
            _saglayici = saglayici;
            _log = log;
        }

        // tamIade: siparişin tamamı mı, tek kalem mi.
        public async Task<GercekIadeSonucu> GonderAsync(
            Order siparis, int? orderItemId, decimal tutar, string? ip)
        {
            var islem = await _context.OdemeIslemleri
                .Where(o => o.OrderId == siparis.Id
                         && o.Durum == OdemeDurumlari.DenemeBasarili
                         && o.IyzicoPaymentId != null)
                .OrderByDescending(o => o.Id)
                .FirstOrDefaultAsync();

            if (islem == null)
            {
                // ⚠️ Eski siparişlerde ödeme sağlayıcı üzerinden
                // alınmamıştı. Akışı burada durdurmak, geçmiş
                // siparişlerin iadesini imkânsız kılardı.
                _log.LogInformation(
                    "Sipariş {Id} için sağlayıcı ödemesi yok; iade yalnızca veritabanında.",
                    siparis.Id);

                return new GercekIadeSonucu(true, IadeYolu.SaglayiciYok, 0m, null);
            }

            var kalemler = await _context.OdemeKalemleri
                .Where(k => k.OdemeIslemiId == islem.Id)
                .ToListAsync();

            var tamIade = orderItemId == null;

            // ⚠️ AYNI GÜN + TAM İADE → cancel.
            // Ekstreye hiç düşmüyor, müşteri "önce çekildi sonra iade
            // edildi" görmüyor. cancel yalnızca TAM tutar destekliyor;
            // aynı gün kısmi iade isteniyorsa yine refund kullanılıyor.
            var ayniGun = islem.TamamlanmaZamani.HasValue
                && islem.TamamlanmaZamani.Value.Date == DateTime.UtcNow.Date;

            if (tamIade && ayniGun && kalemler.All(k => k.IadeEdilenTutar == 0))
            {
                var iptal = await _saglayici.IptalEtAsync(
                    islem.IyzicoPaymentId!, ip, YeniKonusma("ipt", siparis.Id));

                if (!iptal.Basarili)
                {
                    return new GercekIadeSonucu(false, IadeYolu.Iptal, 0m,
                        iptal.HataMesaji ?? "İptal isteği reddedildi.");
                }

                // İptal tüm kalemleri kapatıyor.
                foreach (var kalem in kalemler)
                {
                    kalem.IadeEdilenTutar = kalem.PaidPrice;
                }

                await _context.SaveChangesAsync();

                return new GercekIadeSonucu(true, IadeYolu.Iptal, islem.Price, null);
            }

            // ---- refund yolu ----

            if (kalemler.Count == 0)
            {
                // ⚠️ Kalem kırılımı yoksa kısmi iade yapılamaz. Bu,
                // ödeme anında kalemlerin kaydedilmediği anlamına gelir.
                return new GercekIadeSonucu(false, IadeYolu.Iade, 0m,
                    "Ödemenin kalem kırılımı yok, iyzico'ya iade gönderilemiyor.");
            }

            var hedefler = tamIade
                ? kalemler
                : kalemler.Where(k => k.OrderItemId == orderItemId).ToList();

            if (hedefler.Count == 0)
            {
                return new GercekIadeSonucu(false, IadeYolu.Iade, 0m,
                    "İade edilecek kalem ödeme kaydında bulunamadı.");
            }

            var kalanIhtiyac = tutar;
            var gonderilen = 0m;

            foreach (var kalem in hedefler)
            {
                if (kalanIhtiyac <= 0)
                {
                    break;
                }

                var kalanHak = kalem.PaidPrice - kalem.IadeEdilenTutar;

                if (kalanHak <= 0)
                {
                    continue;
                }

                var gonderilecek = Math.Min(kalanHak, kalanIhtiyac);

                var sonuc = await _saglayici.IadeEtAsync(
                    kalem.IyzicoPaymentTransactionId, gonderilecek, ip,
                    YeniKonusma("iad", siparis.Id));

                if (!sonuc.Basarili)
                {
                    // ⚠️ Kısmen gönderilmiş olabilir. Başarılıları
                    // ANINDA kaydediyoruz: admin tekrar denediğinde
                    // aynı para ikinci kez gönderilmesin.
                    await _context.SaveChangesAsync();

                    _log.LogError(
                        "İade kısmen başarısız. siparis: {Id}, gönderilen: {Gonderilen}, hata: {Hata}",
                        siparis.Id, gonderilen, sonuc.HataMesaji);

                    return new GercekIadeSonucu(false, IadeYolu.Iade, gonderilen,
                        $"{gonderilen:N2} TL gönderildi, kalanı reddedildi: " +
                        (sonuc.HataMesaji ?? "bilinmeyen hata"));
                }

                kalem.IadeEdilenTutar += gonderilecek;
                gonderilen += gonderilecek;
                kalanIhtiyac -= gonderilecek;
            }

            await _context.SaveChangesAsync();

            if (kalanIhtiyac > 0)
            {
                // ⚠️ Hesaplanan tutar, ödemede kalan haktan fazla.
                // Sessizce eksik ödemek yerine söylüyoruz.
                _log.LogWarning(
                    "İade tutarı ödemede kalan haktan fazla. siparis: {Id}, eksik: {Eksik}",
                    siparis.Id, kalanIhtiyac);

                return new GercekIadeSonucu(false, IadeYolu.Iade, gonderilen,
                    $"{gonderilen:N2} TL iade edildi ama {kalanIhtiyac:N2} TL " +
                    "ödemede karşılığı yok.");
            }

            return new GercekIadeSonucu(true, IadeYolu.Iade, gonderilen, null);
        }

        // Her istek için ayrı eşleştirme anahtarı; iyzico aynı anahtarla
        // ikinci iadeyi reddediyor ve bu bizim işimize yarıyor.
        private static string YeniKonusma(string onek, int siparisId) =>
            $"{onek}{siparisId}-{Guid.NewGuid().ToString("N")[..12]}";
    }
}
