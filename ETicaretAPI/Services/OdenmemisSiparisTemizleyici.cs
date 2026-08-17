using ETicaretAPI.Data;
using ETicaretAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ETicaretAPI.Services
{
    // ⭐ YENİ — ödenmemiş siparişi iptal edip rezervasyonu geri verir.
    //
    // İki tüketici var: yeni sipariş isteği (kullanıcının önceki bekleyen
    // siparişini iptal ediyor) ve süre aşımı işi. İkisi de aynı üç şeyi
    // geri almak zorunda: stok, kupon sayacı, kupon kullanım kaydı.
    // Kopyalasaydık biri unutulur ve stok sessizce kaybolurdu.
    public class OdenmemisSiparisTemizleyici
    {
        private readonly AppDbContext _context;
        private readonly StokDefteri _defter;
        private readonly ILogger<OdenmemisSiparisTemizleyici> _log;

        public OdenmemisSiparisTemizleyici(
            AppDbContext context,
            StokDefteri defter,
            ILogger<OdenmemisSiparisTemizleyici> log)
        {
            _context = context;
            _defter = defter;
            _log = log;
        }

        // Kendi transaction'ını açar. Çağıran başka bir transaction
        // içindeyse bunu ÇAĞIRMAMALI — iç içe transaction desteklenmiyor.
        public async Task<bool> IptalEtAsync(int siparisId, string sebep)
        {
            // ⚠️ Durum koşulu SORGUYA dahil: ödenmiş bir siparişi bu
            // yoldan iptal etmek stoğu geri verip parayı bırakmak olurdu.
            var siparis = await _context.Orders.FirstOrDefaultAsync(
                o => o.Id == siparisId && o.Status == SiparisDurumlari.OdemeBekliyor);

            if (siparis == null)
            {
                return false;
            }

            var kalemler = await _context.OrderItems
                .Where(oi => oi.OrderId == siparisId)
                .OrderBy(oi => oi.ProductId)   // kilit sırası — deadlock önlemi
                .ToListAsync();

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1) Stoğu geri ver + deftere yaz
                foreach (var kalem in kalemler)
                {
                    var urun = await _context.Products.FindAsync(kalem.ProductId);

                    if (urun == null)
                    {
                        continue;
                    }

                    // Hareket, stok değişmeden önce yazılıyor; sonra
                    // yazsak "önceki stok" zaten artmış olurdu.
                    _defter.Ekle(
                        urunId: urun.Id,
                        miktar: kalem.Quantity,
                        oncekiStok: urun.Stock,
                        sebep: StokSebep.IptalIadesi,
                        kullaniciId: null,
                        referansTipi: "Order",
                        referansId: siparis.Id,
                        aciklama: sebep);

                    urun.Stock += kalem.Quantity;
                }

                // 2) Kupon hakkını geri ver.
                //
                // ⚠️ Sayaç ExecuteUpdate ile azaltılıyor: sipariş anında
                // da öyle artırılmıştı. Bellekte azaltsaydık aynı kolona
                // iki kez yazılırdı.
                if (!string.IsNullOrWhiteSpace(siparis.CouponCode))
                {
                    var kullanimlar = await _context.CouponUsages
                        .Where(cu => cu.OrderId == siparis.Id)
                        .ToListAsync();

                    foreach (var kullanim in kullanimlar)
                    {
                        await _context.Coupons
                            .Where(c => c.Id == kullanim.CouponId && c.UsedCount > 0)
                            .ExecuteUpdateAsync(s => s.SetProperty(
                                c => c.UsedCount, c => c.UsedCount - 1));
                    }

                    // Kayıt silinmezse kişi başı limit dolu kalır ve
                    // müşteri kuponu bir daha kullanamaz.
                    _context.CouponUsages.RemoveRange(kullanimlar);
                }

                // 3) Bekleyen ödeme denemelerini kapat.
                await _context.OdemeIslemleri
                    .Where(o => o.OrderId == siparis.Id
                             && o.Durum == OdemeDurumlari.DenemeBaslatildi)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.Durum, OdemeDurumlari.DenemeSuresiDoldu)
                        .SetProperty(o => o.TamamlanmaZamani, DateTime.UtcNow));

                // 4) Siparişi iptal et.
                //
                // ⚠️ PaymentStatus "iade_edildi" DEĞİL: para hiç
                // çekilmedi, iade edilecek bir şey yok. "iade_edildi"
                // yazmak iade raporlarını kirletirdi.
                siparis.Status = SiparisDurumlari.Iptal;
                siparis.PaymentStatus = OdemeDurumlari.Basarisiz;
                siparis.CancelReason = sebep;
                siparis.CancelledAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _log.LogInformation(
                    "Ödenmemiş sipariş iptal edildi. siparisId: {Id}, sebep: {Sebep}",
                    siparis.Id, sebep);

                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
