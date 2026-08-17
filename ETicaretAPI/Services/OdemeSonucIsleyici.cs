using ETicaretAPI.Data;
using ETicaretAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ETicaretAPI.Services
{
    public enum OdemeSonucTipi
    {
        Bilinmiyor,     // token bize ait değil
        DevamEdiyor,    // müşteri hâlâ ödeme sayfasında
        Basarili,
        Incelemede,     // iyzico fraud kontrolünde, para kesin değil
        Basarisiz,
        ZatenIslendi,
        SorguHatasi     // iyzico'ya ulaşılamadı; sipariş değişmedi
    }

    public record OdemeSonucu(OdemeSonucTipi Tip, string Mesaj, int? SiparisId = null);


    // ============================================================
    //  ⭐ YENİ — ÖDEME SONUCUNU SİPARİŞE UYGULAYAN TEK NOKTA
    //
    //  Üç yerden çağrılıyor: callback (kullanıcının tarayıcısı),
    //  webhook (sunucudan sunucuya) ve simülasyon sayfası. Hangisi
    //  önce gelirse işi o bitiriyor, diğerleri "zaten işlendi" alıyor.
    //
    //  ⚠️ Sonuç İSTEMCİDEN alınmıyor. Callback'te gelen token yalnızca
    //  bir anahtar; ödemenin gerçekten olup olmadığı sağlayıcıya
    //  sorularak öğreniliyor. Aksi hâlde müşteri "ödendi" gönderip
    //  bedava alışveriş yapardı.
    // ============================================================
    public class OdemeSonucIsleyici
    {
        private readonly AppDbContext _context;
        private readonly IOdemeSaglayici _saglayici;
        private readonly IEmailGonderici _email;
        private readonly EmailSablonlari _sablonlar;
        private readonly SistemGunlugu _gunluk;
        private readonly ILogger<OdemeSonucIsleyici> _log;

        public OdemeSonucIsleyici(
            AppDbContext context,
            IOdemeSaglayici saglayici,
            IEmailGonderici email,
            EmailSablonlari sablonlar,
            SistemGunlugu gunluk,
            ILogger<OdemeSonucIsleyici> log)
        {
            _context = context;
            _saglayici = saglayici;
            _email = email;
            _sablonlar = sablonlar;
            _gunluk = gunluk;
            _log = log;
        }


        public async Task<OdemeSonucu> IsleAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return new OdemeSonucu(OdemeSonucTipi.Bilinmiyor, "Token boş.");
            }

            var islem = await _context.OdemeIslemleri
                .OrderByDescending(o => o.Id)
                .FirstOrDefaultAsync(o => o.Token == token);

            if (islem == null)
            {
                // ⚠️ Bilinmeyen token normal olabilir: başka bir işyerine
                // ait webhook ya da eski bir deneme. Hata sayılmıyor.
                _log.LogWarning("Bilinmeyen ödeme token'ı geldi.");
                return new OdemeSonucu(OdemeSonucTipi.Bilinmiyor, "Bu ödeme kaydı bulunamadı.");
            }

            if (islem.Durum == OdemeDurumlari.DenemeBasarili)
            {
                return new OdemeSonucu(OdemeSonucTipi.ZatenIslendi,
                    "Ödeme daha önce onaylanmıştı.", islem.OrderId);
            }

            var sorgu = await _saglayici.SorgulaAsync(token, islem.ConversationId);

            if (!sorgu.CagriBasarili)
            {
                // ⚠️ Sipariş DEĞİŞTİRİLMİYOR. "Sorgu başarısız" ile
                // "ödeme başarısız" farklı şeyler; ikisini karıştırmak
                // ödenmiş bir siparişi iptal etmeye yol açardı.
                // Webhook aynı sonucu tekrar getirecek.
                return new OdemeSonucu(OdemeSonucTipi.SorguHatasi,
                    sorgu.HataMesaji ?? "Ödeme durumu sorgulanamadı.", islem.OrderId);
            }

            // Müşteri hâlâ 3DS ekranında olabilir.
            if (!sorgu.OdemeBasarili && string.IsNullOrWhiteSpace(sorgu.HataKodu)
                && !string.Equals(sorgu.OdemeDurumu, "FAILURE", StringComparison.OrdinalIgnoreCase))
            {
                return new OdemeSonucu(OdemeSonucTipi.DevamEdiyor,
                    "Ödeme henüz tamamlanmadı.", islem.OrderId);
            }

            islem.HamCevap = sorgu.HamCevap;
            islem.MdStatus = sorgu.MdStatus;
            islem.FraudDurumu = sorgu.FraudDurumu;
            islem.IyzicoPaymentId = sorgu.PaymentId;
            islem.PaidPrice = sorgu.PaidPrice;
            islem.Taksit = sorgu.Taksit;
            islem.KartTipi = sorgu.KartTipi;
            islem.KartAilesi = sorgu.KartAilesi;
            islem.BinNumarasi = sorgu.BinNumarasi;
            islem.Son4Hane = sorgu.Son4Hane;

            if (!sorgu.OdemeBasarili)
            {
                return await BasarisizYazAsync(islem, sorgu);
            }

            // ---- Buradan sonrası: iyzico "ödendi" dedi ----

            var siparis = await _context.Orders.FirstOrDefaultAsync(o => o.Id == islem.OrderId);

            if (siparis == null)
            {
                // Sipariş silinmiş — parayı bizde tutamayız.
                await ParayiGeriVerAsync(islem, sorgu, "Ödemesi gelen sipariş bulunamadı.");
                return new OdemeSonucu(OdemeSonucTipi.Basarisiz, "Sipariş bulunamadı, ödeme iade edildi.");
            }

            // ⚠️ TUTAR DOĞRULAMASI. Fiyatı biz gönderdik, eşleşmemesi
            // beklenmez; eşleşmiyorsa parayı almaya devam etmek yerine
            // geri veriyoruz ve kayda geçiyoruz.
            if (sorgu.Price.HasValue && sorgu.Price.Value != islem.Price)
            {
                await ParayiGeriVerAsync(islem, sorgu,
                    $"Tutar uyuşmuyor: beklenen {islem.Price}, gelen {sorgu.Price}.");

                return new OdemeSonucu(OdemeSonucTipi.Basarisiz,
                    "Ödeme tutarı doğrulanamadı, işlem iade edildi.", siparis.Id);
            }

            // ⚠️ GEÇ GELEN ÖDEME. Süre aşımından iptal edilmiş bir
            // siparişe ödeme düşebilir. Sessizce yok saymak müşterinin
            // parasını almak demektir.
            if (siparis.Status == SiparisDurumlari.Iptal)
            {
                await ParayiGeriVerAsync(islem, sorgu,
                    "Ödeme, iptal edilmiş siparişe geldi (süre aşımı sonrası).");

                return new OdemeSonucu(OdemeSonucTipi.Basarisiz,
                    "Sipariş süresi dolduğu için iptal edilmişti; ödemen iade edildi.",
                    siparis.Id);
            }

            // ⚠️ fraudStatus = 0 → para KESİN DEĞİL, inceleme sürüyor.
            // Bunu "odendi" saymak, ret gelirse ürünü kargoya vermiş
            // olmak demek. Sipariş hattı beklemede kalıyor.
            var incelemede = sorgu.FraudDurumu == 0;

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                islem.Durum = OdemeDurumlari.DenemeBasarili;
                islem.TamamlanmaZamani = DateTime.UtcNow;

                // Kalem kırılımı — kısmi iade bunsuz yapılamaz.
                await OdemeKalemleriniYazAsync(islem, sorgu);

                siparis.CardLast4 = sorgu.Son4Hane ?? siparis.CardLast4;

                if (incelemede)
                {
                    // Durum odeme_bekliyor KALIYOR; süre aşımı işi
                    // "odeme_incelemede" olanları atlıyor.
                    siparis.PaymentStatus = OdemeDurumlari.Incelemede;
                }
                else
                {
                    siparis.Status = SiparisDurumlari.Hazirlaniyor;
                    siparis.PaymentStatus = OdemeDurumlari.Odendi;
                }

                // ⚠️ Amount = siparişin Total'i, paidPrice DEĞİL.
                // Aradaki fark banka taksit komisyonu; ciroya girmez.
                _context.Payments.Add(new Payment
                {
                    OrderId = siparis.Id,
                    UserId = siparis.UserId,
                    Amount = siparis.Total,
                    CardLast4 = sorgu.Son4Hane ?? string.Empty,
                    Status = incelemede ? "beklemede" : "basarili",
                    PaidAt = DateTime.UtcNow
                });

                await KartiSaklaAsync(siparis.UserId, sorgu);

                // ⚠️ SEPET BURADA TEMİZLENİYOR, sipariş oluşurken değil.
                // Ödeme başarısız olsaydı müşteri hem siparişsiz hem
                // sepetsiz kalırdı.
                var sepet = await _context.CartItems
                    .Where(c => c.UserId == siparis.UserId)
                    .ToListAsync();

                _context.CartItems.RemoveRange(sepet);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            // ⚠️ Mail commit'ten SONRA: geri alınamaz yan etki en sona.
            await SiparisMailiGonderAsync(siparis);

            return incelemede
                ? new OdemeSonucu(OdemeSonucTipi.Incelemede,
                    "Ödemen alındı, banka doğrulaması sürüyor.", siparis.Id)
                : new OdemeSonucu(OdemeSonucTipi.Basarili,
                    "Ödemen alındı, siparişin hazırlanıyor.", siparis.Id);
        }


        // ---------- yardımcılar ----------

        private async Task<OdemeSonucu> BasarisizYazAsync(
            OdemeIslemi islem, OdemeSorguSonucu sorgu)
        {
            islem.Durum = OdemeDurumlari.DenemeBasarisiz;
            islem.TamamlanmaZamani = DateTime.UtcNow;
            islem.HataKodu = sorgu.HataKodu;
            islem.HataMesaji = sorgu.HataMesaji;

            // ⚠️ Sipariş "iptal" YAPILMIYOR, stok da geri verilmiyor:
            // müşteri başka kartla tekrar denesin. Süre aşımı işi
            // denemezse toplar.
            await _context.Orders
                .Where(o => o.Id == islem.OrderId
                         && o.Status == SiparisDurumlari.OdemeBekliyor)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    o => o.PaymentStatus, OdemeDurumlari.Basarisiz));

            await _context.SaveChangesAsync();

            return new OdemeSonucu(OdemeSonucTipi.Basarisiz,
                sorgu.HataMesaji ?? "Ödeme alınamadı.", islem.OrderId);
        }


        // Alınmaması gereken parayı geri verir ve kayda geçirir.
        private async Task ParayiGeriVerAsync(
            OdemeIslemi islem, OdemeSorguSonucu sorgu, string sebep)
        {
            var iadeSonucu = "denenmedi";

            if (!string.IsNullOrWhiteSpace(sorgu.PaymentId))
            {
                var sonuc = await _saglayici.IptalEtAsync(
                    sorgu.PaymentId!, null, "gv-" + Guid.NewGuid().ToString("N")[..12]);

                iadeSonucu = sonuc.Basarili
                    ? "iade edildi"
                    : $"iade BAŞARISIZ ({sonuc.HataMesaji})";
            }

            islem.Durum = OdemeDurumlari.DenemeBasarisiz;
            islem.TamamlanmaZamani = DateTime.UtcNow;
            islem.HataKodu = "GERI_VERILDI";
            islem.HataMesaji = $"{sebep} Sonuç: {iadeSonucu}";

            await _context.SaveChangesAsync();

            // ⚠️ Kayda geçmesi şart: burası insan müdahalesi gerekebilen
            // tek yer. Sessiz kalırsa müşterinin parası kaybolur.
            _log.LogError("Ödeme geri verildi. islemId: {Id}, sebep: {Sebep}, sonuç: {Sonuc}",
                islem.Id, sebep, iadeSonucu);

            await _gunluk.HataYazAsync(
                yol: "/api/odeme/callback",
                yontem: "POST",
                mesaj: $"Ödeme geri verildi (islem {islem.Id}): {sebep} Sonuç: {iadeSonucu}",
                yiginIzi: sorgu.HamCevap,
                kullaniciId: islem.UserId,
                ip: null);
        }


        private async Task OdemeKalemleriniYazAsync(OdemeIslemi islem, OdemeSorguSonucu sorgu)
        {
            // Tekrar gelen bildirimde ikinci kez yazılmasın.
            var varOlan = await _context.OdemeKalemleri
                .AnyAsync(k => k.OdemeIslemiId == islem.Id);

            if (varOlan)
            {
                return;
            }

            foreach (var kalem in sorgu.Kalemler)
            {
                // Kargo satırında ItemId sayı değil ("kargo") → null.
                var orderItemId = int.TryParse(kalem.ItemId, out var id)
                    ? id
                    : (int?)null;

                _context.OdemeKalemleri.Add(new OdemeKalemi
                {
                    OdemeIslemiId = islem.Id,
                    OrderItemId = orderItemId,
                    IyzicoPaymentTransactionId = kalem.PaymentTransactionId,
                    Price = kalem.Price,
                    PaidPrice = kalem.PaidPrice
                });
            }
        }


        // Müşteri ödeme sayfasında "kartımı kaydet" dediyse iyzico
        // jetonları döndürüyor. PAN yine bize gelmiyor.
        private async Task KartiSaklaAsync(int userId, OdemeSorguSonucu sorgu)
        {
            if (string.IsNullOrWhiteSpace(sorgu.CardToken)
                || string.IsNullOrWhiteSpace(sorgu.CardUserKey))
            {
                return;
            }

            var kullanici = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (kullanici == null)
            {
                return;
            }

            kullanici.IyzicoCardUserKey ??= sorgu.CardUserKey;

            var zatenVar = await _context.Cards
                .AnyAsync(c => c.UserId == userId && c.IyzicoCardToken == sorgu.CardToken);

            if (zatenVar)
            {
                return;
            }

            _context.Cards.Add(new Card
            {
                UserId = userId,

                // ⚠️ Kart sahibi adı iyzico'dan gelmiyor; kullanıcının
                // adını yazmak uydurma olurdu. Kart ailesi gösteriliyor.
                CardHolderName = sorgu.KartAilesi ?? "Kayıtlı kart",
                Last4Digits = sorgu.Son4Hane ?? "",
                CardType = sorgu.KartTipi ?? "",
                BinNumarasi = sorgu.BinNumarasi,
                IyzicoCardToken = sorgu.CardToken,

                // ⚠️ Son kullanma tarihi de gelmiyor. 0 bırakıyoruz —
                // uydurma bir tarih yazmak "kart geçerli" iddiası olurdu.
                ExpiryMonth = 0,
                ExpiryYear = 0
            });
        }


        private async Task SiparisMailiGonderAsync(Order siparis)
        {
            var alici = await _context.Users
                .Where(u => u.Id == siparis.UserId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync() ?? string.Empty;

            var kalemler = await _context.OrderItems
                .Where(oi => oi.OrderId == siparis.Id)
                .Select(oi => new EmailSiparisKalemi(oi.ProductName, oi.Quantity, oi.UnitPrice))
                .ToListAsync();

            await _email.GuvenliGonderAsync(
                _log, alici, _sablonlar.SiparisAlindi(siparis, kalemler), "SiparisAlindi");
        }
    }
}
