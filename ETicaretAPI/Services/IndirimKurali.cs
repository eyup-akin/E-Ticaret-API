namespace ETicaretAPI.Services
{
    // ⭐ YENİ (B1 tamamlama, 2026-08-12)
    //
    // İNDİRİM ÖNCESİ FİYATIN TEK DOĞRU KAYNAĞI.
    //
    // ⚠️ Kural önce `ProductsController` içinde private bir metottu ve
    // orada iki tüketicisi vardı (ekle / güncelle). Excel içe aktarma
    // üçüncü tüketici olunca buraya taşındı — kural iki dosyada ayrı
    // ayrı yazılsaydı, panelden reddedilen bir eski fiyat Excel'den
    // geçebilir hale gelirdi.
    public static class IndirimKurali
    {
        // Tek kural: eski fiyat, satış fiyatından BÜYÜK olmalı.
        // Eşit ya da küçükse ortada indirim yok demektir.
        //
        // ⚠️ HATA DÖNDÜRMÜYOR, SESSİZCE NULL'A ÇEKİYOR.
        // Admin "eski fiyat 100, yeni fiyat 150" yazdıysa niyeti
        // indirim değil; bunu hata sayıp formu (ya da 500 satırlık
        // Excel'i) reddetmek, işi olmayan bir engel çıkarmak olurdu.
        // Alan boşalıyor ve ürün indirimsiz kaydediliyor — sonuç
        // ekranda hemen görünüyor.
        //
        // ⚠️⚠️ YASAL DENETİM BURADA YOK.
        // Fiyat Etiketi Yönetmeliği indirim öncesi fiyatın son 30
        // günde fiilen uygulanmış en düşük fiyat olmasını istiyor.
        // Bunu doğrulamak fiyat geçmişi tutmayı gerektiriyor ve o
        // Aşama 10'un işi. Bugün admin ne yazarsa o görünüyor.
        public static decimal? EskiFiyatiDogrula(decimal? eskiFiyat, decimal fiyat)
        {
            if (!eskiFiyat.HasValue || eskiFiyat.Value <= fiyat)
            {
                return null;
            }

            return eskiFiyat;
        }
    }
}
