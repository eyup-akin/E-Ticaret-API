using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Services;

namespace ETicaretAPI.Support
{
    // ⭐ YENİ — LOG EKRANLARININ ORTAK SORGU KURALLARI
    //
    // Dört sekme (denetim / e-posta / giriş / hata) aynı sayfalama, aynı
    // varsayılan tarih aralığı ve aynı sayım kuralını kullanıyor.
    // Kopyalasaydık üst sınır bir yerde değişip diğerlerinde kalırdı —
    // ve fark ancak tablo şiştiğinde görülürdü.
    public static class LogSorgusu
    {
        // ⚠️ TOPLAM SAYIM ÜST SINIRI.
        // "Toplam 240.000 kayıt" yazmak her sayfa yüklemesinde tam tarama
        // demek. Sınıra takılırsa ekran "1000+" gösteriyor; kesin sayı
        // kimsenin işine yaramıyor, sayfalama zaten çalışıyor.
        public const int SayimUstSiniri = 1000;

        // ⚠️ VARSAYILAN ARALIK 7 GÜN.
        // RaporTarihi'nin varsayılanı 30 gün ve raporlar için doğru; log
        // tabloları sipariş tablosundan kat kat hızlı büyüyor. 30 gün
        // deseydik uygulamanın en pahalı sorgusu SAYFANIN İLK AÇILIŞI olurdu.
        public const int VarsayilanGun = 7;

        // ⚠️ Sayfa numarasının üst sınırı SayfaSiniri'nde — taşma
        // koruması dokuz uçta daha gerekiyordu, tek kopya kaldı.
        //
        // ⚠️ pageSize ÜST SINIRI SUNUCUDA ZORLANIYOR. "?pageSize=100000"
        // yazan bir istek, sayfalamanın koruduğu her şeyi geçersiz kılardı.
        public static (int Sayfa, int Boyut) SayfaDuzelt(int page, int pageSize)
        {
            var sayfa = SayfaSiniri.Duzelt(page);
            var boyut = pageSize < 1 || pageSize > 100 ? 20 : pageSize;

            return (sayfa, boyut);
        }

        public static RaporAraligi Aralik(
            RaporTarihi tarih, DateTime? baslangic, DateTime? bitis)
        {
            if (baslangic == null && bitis == null)
            {
                var bugun = tarih.YereleCevir(DateTime.UtcNow).Date;

                // -(VarsayilanGun - 1): bugün de sayılıyor. -7 yazsaydık
                // 8 günlük aralık çıkardı (klasik çit direği hatası).
                baslangic = bugun.AddDays(-(VarsayilanGun - 1));
                bitis = bugun;
            }

            return tarih.Aralik(baslangic, bitis);
        }

        // ⚠️ ÜST SINIRLI SAYIM. Take(sınır + 1) ile sayılıyor: sınır
        // aşılırsa SQL Server geri kalanı hiç saymıyor.
        public static async Task<(int Toplam, bool Asildi)> SayAsync<T>(IQueryable<T> sorgu)
        {
            var sayi = await sorgu.Take(SayimUstSiniri + 1).CountAsync();

            return sayi > SayimUstSiniri
                ? (SayimUstSiniri, true)
                : (sayi, false);
        }
    }
}
