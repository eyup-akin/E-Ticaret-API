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

            // ⚠️ İndirim orantılı düşülüyor: indirim sipariş seviyesinde
            // uygulanıyor, ham fiyatı geri ödersek müşteri indirim payını
            // da cebe atar.
            //
            // ⭐ DEĞİŞTİ — DiscountAmount değil ToplamIndirim.
            //
            // Eskiden yalnızca kupon indirimi (DiscountAmount) düşülüyordu.
            // Kombin indirimi (Order.KombinIndirimi) ayrı bir alanda
            // tutuluyor ve bu hesap onu HİÇ görmüyordu: kombin indirimli
            // bir siparişten tek kalem iade edildiğinde müşteriye indirim
            // payı düşülmeden ödeme yapılıyordu.
            //
            // Örnek: A (400) + B (600), %10 kombin indirimi.
            //   SubTotal 1000, KombinIndirimi 100.
            //   A iade edilince eski hesap 400 TL ödüyordu; doğrusu 360.
            //
            // Order.ToplamIndirim iki alanı topluyor, yani yarın üçüncü
            // bir indirim eklenirse burası kendiliğinden doğru kalır.
            var indirimPayi = 0m;

            if (siparis.ToplamIndirim > 0 && siparis.SubTotal > 0)
            {
                indirimPayi = Math.Round(
                    siparis.ToplamIndirim * kalemToplami / siparis.SubTotal,
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
