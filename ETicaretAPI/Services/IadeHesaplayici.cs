using ETicaretAPI.Models;

namespace ETicaretAPI.Services
{
    // ⭐ YENİ (Aşama 9) — İADE TUTARI, TEK YERDE
    //
    // Üç tüketicisi var (müşteri ekranı, admin ekranı, para iadesi anı).
    // Ayrı ayrı hesaplasalardı söylenen tutar ile ödenen tutar ayrışırdı.
    public class IadeHesaplayici
    {
        /// <summary>
        /// Geri ödenecek tutar. `kalem` null ise siparişin tamamı.
        /// </summary>
        public decimal Hesapla(Order siparis, OrderItem? kalem)
        {
            // Tüm sipariş: müşterinin ödediği her şey, kargo dahil.
            // Cayma hakkında standart teslimat masrafı da iade edilir.
            // ⚠️ Geri gönderim kargosu modellenmiş değil, hesaba girmiyor.
            if (kalem == null)
            {
                return siparis.Total;
            }

            // Fiyat OrderItem'dan (dondurulmuş), ürünün bugünkü fiyatından değil.
            var kalemToplami = kalem.UnitPrice * kalem.Quantity;

            // ⚠️ Kupon indirimi orantılı düşülüyor: indirim sipariş
            // seviyesinde uygulanıyor, ham fiyatı geri ödersek müşteri
            // indirim payını da cebe atar.
            var indirimPayi = 0m;

            if (siparis.DiscountAmount > 0 && siparis.SubTotal > 0)
            {
                indirimPayi = Math.Round(
                    siparis.DiscountAmount * kalemToplami / siparis.SubTotal,
                    2,
                    MidpointRounding.AwayFromZero);
            }

            // Kargo iade edilmiyor: sipariş yine gönderildi, diğer
            // ürünler müşteride kaldı.
            var tutar = kalemToplami - indirimPayi;

            return tutar < 0 ? 0m : tutar;
        }
    }
}
