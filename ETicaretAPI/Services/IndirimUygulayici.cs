using ETicaretAPI.Models;

namespace ETicaretAPI.Services
{
    // ⭐ YENİ — ÜRÜNE İNDİRİM UYGULAMA HESABI, TEK YERDE
    //
    // ⚠️ YENİ BİR ALAN AÇILMADI. İndirim zaten `EskiFiyat` + `Price`
    // ikilisiyle anlatılıyor (B1) ve mobil taraf yüzdeyi bu iki sayıdan
    // türetiyor. Bir `IndirimOrani` sütunu eklemek aynı gerçeği ikinci
    // kez saklamak olurdu: fiyat elle değiştirildiğinde oran bayatlar ve
    // ekranda "%20 indirim" yazan ama fiyatı %12 düşük bir ürün çıkardı.
    //
    // ⚠️ Panel yüzde/tutar giriyor, veritabanı FİYAT saklıyor. Dönüşüm
    // burada, tek noktada. Panelde hesaplasaydık kural istemciye kaçar
    // ve toplu indirim ikinci bir kopya doğururdu.
    public static class IndirimUygulayici
    {
        public const string TipYuzde = "yuzde";
        public const string TipTutar = "tutar";

        // ⚠️ ÜST SINIR VAR. %90'ı aşan bir indirim neredeyse her zaman
        // yazım hatasıdır (90 yerine 900). Yasak koymak, "1.999 TL'lik
        // ürünü 20 TL'ye sattık" kazasından ucuza gelir. Gerçekten
        // gerekiyorsa admin fiyatı doğrudan ürün formundan değiştirir.
        private const decimal MaxYuzde = 90m;

        public sealed record Sonuc(decimal YeniFiyat, decimal EskiFiyat, bool MaliyetinAltinda);

        // İndirim tabanı: ürün ZATEN indirimliyse taban eski fiyattır.
        //
        // ⚠️⚠️ BU SATIR SESSİZ BİR HATANIN ÖNÜNÜ ALIYOR.
        //
        // Taban olarak güncel fiyatı alsaydık, %20 indirimli bir ürüne
        // ikinci kez "%20 indirim" uygulandığında taban 80'e düşer ve
        // sonuç 64 olurdu — yani admin oranı DEĞİŞTİRMEK isterken
        // indirimi ÜST ÜSTE BİNDİRMİŞ olurdu. Panelde "%20" yazarken
        // ekranda "%36 indirim" görünürdü.
        //
        // Eski fiyat = ürünün indirimsiz gerçek fiyatı. Taban odur.
        public static decimal Taban(Product urun)
        {
            return urun.EskiFiyat.HasValue && urun.EskiFiyat.Value > urun.Price
                ? urun.EskiFiyat.Value
                : urun.Price;
        }

        // Hesabı yapar. Hata varsa mesajı döner, sonuç null olur.
        public static (Sonuc? sonuc, string? hata) Hesapla(Product urun, string tip, decimal deger)
        {
            if (urun.ArsivlendiMi)
            {
                return (null, "Arşivlenmiş ürüne indirim uygulanamaz.");
            }

            var taban = Taban(urun);

            if (taban <= 0)
            {
                return (null, "Ürünün fiyatı sıfır; indirim uygulanamaz.");
            }

            decimal yeniFiyat;

            if (tip == TipYuzde)
            {
                if (deger > MaxYuzde)
                {
                    return (null, $"İndirim oranı en fazla %{MaxYuzde:0} olabilir.");
                }

                yeniFiyat = taban * (1 - deger / 100m);
            }
            else if (tip == TipTutar)
            {
                if (deger >= taban)
                {
                    return (null, "İndirim tutarı ürünün fiyatından küçük olmalı.");
                }

                yeniFiyat = taban - deger;
            }
            else
            {
                return (null, "İndirim tipi 'yuzde' ya da 'tutar' olmalı.");
            }

            // ⚠️ AŞAĞI YUVARLANIYOR, en yakına değil.
            //
            // Yukarı yuvarlamak, müşteriye söylenen orandan bir kuruş
            // DAHA AZ indirim vermek demek — yani ekrandaki "%20"
            // ifadesini yalanlamak. Aşağı yuvarlamak en fazla mağazanın
            // bir kuruş zararına. Aynı gerekçeyle mobilde yüzde de
            // aşağı yuvarlanıyor (utils/indirim.js): indirim bir
            // reklamdır ve reklamda abartma yasal risk taşır.
            yeniFiyat = Math.Floor(yeniFiyat * 100m) / 100m;

            if (yeniFiyat <= 0)
            {
                return (null, "İndirimli fiyat sıfırın altına düşüyor.");
            }

            // ⚠️ EşİTLİK DE HATA: indirim sonrası fiyat tabana eşitse
            // ortada indirim yok demektir ve `EskiFiyat` yazmak ekranda
            // üstü çizili AYNI sayıyı gösterirdi.
            if (yeniFiyat >= taban)
            {
                return (null, "Bu değer gerçek bir indirim üretmiyor.");
            }

            // ⚠️ MALİYETİN ALTI ENGELLENMİYOR, YALNIZCA BİLDİRİLİYOR.
            //
            // Zararına satış bilinçli bir kampanya olabilir (stok
            // eritme, müşteri kazanma). Engellemek, panelin bilmediği
            // bir iş kararını dayatmak olurdu. Ama sessiz kalmak da
            // yanlış: admin bunu fark etmeden yapabilir.
            var maliyetinAltinda = urun.Cost.HasValue && yeniFiyat < urun.Cost.Value;

            return (new Sonuc(yeniFiyat, taban, maliyetinAltinda), null);
        }

        // Ürünü yerinde günceller. SaveChanges ÇAĞIRMAZ.
        //
        // ⚠️ StokDefteri ile aynı gerekçe: çağıran işlem kendi
        // transaction'ında yazsın diye. Toplu indirimde 50 ürünü tek
        // SaveChanges ile yazmak, 50 ayrı yazmadan hem hızlı hem
        // atomik.
        public static void Uygula(Product urun, Sonuc sonuc)
        {
            urun.EskiFiyat = sonuc.EskiFiyat;
            urun.Price = sonuc.YeniFiyat;
        }

        // İndirimi kaldırır: fiyat indirimsiz haline döner.
        //
        // ⚠️ Ürün indirimli değilse HİÇBİR ŞEY YAPMIYOR ve false
        // dönüyor. `Price = EskiFiyat` demek, EskiFiyat null iken
        // fiyatı sıfırlamak olurdu.
        public static bool Kaldir(Product urun)
        {
            if (!urun.EskiFiyat.HasValue || urun.EskiFiyat.Value <= urun.Price)
            {
                return false;
            }

            urun.Price = urun.EskiFiyat.Value;
            urun.EskiFiyat = null;

            return true;
        }
    }
}
