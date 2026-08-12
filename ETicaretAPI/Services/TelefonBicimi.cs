using System.Text;

namespace ETicaretAPI.Services
{
    // ⭐ YENİ (4.9) — TELEFON NORMALİZASYONU VE GÖSTERİMİ
    //
    // ⚠️ NEDEN ORTAK BİR YERDE, NEDEN CONTROLLER'IN İÇİNDE DEĞİL?
    // Üç tüketicisi var: PhonesController (kaydederken normalize
    // eder, listelerken gösterir), AddressesController (adresin
    // numarasını gösterir) ve OrdersController (sipariş anında
    // gösterim biçimini dondurur). "Kural tek yerde kullanılıyorsa
    // orada durur; ikinci tüketici çıktığı an ortak yere taşınır."
    //
    // ⚠️ SERVİS DEĞİL STATIC — DI'ya kaydedilmiyor. Sınıfın hiçbir
    // bağımlılığı ve hiçbir durumu yok; saf fonksiyon. DI'ya
    // koymak her controller'ın constructor'ına bir parametre daha
    // eklemekten başka bir şey getirmezdi.
    public static class TelefonBicimi
    {
        // Türkiye'de alan kodu dahil hane sayısı: 10 (5xx xxx xx xx
        // ya da 212 xxx xx xx). Sakladığımız kanonik biçim bu.
        private const int HaneSayisi = 10;

        /// <summary>
        /// Kullanıcının yazdığı metni kanonik 10 haneye indirir.
        /// Çevrilemiyorsa null döner — çağıran reddetmelidir.
        /// </summary>
        public static string? Normalize(string? ham)
        {
            if (string.IsNullOrWhiteSpace(ham)) return null;

            // 1) Rakam dışındaki her şeyi at: boşluk, tire, parantez,
            //    nokta, artı. Müşterinin nasıl yazdığı bizi
            //    ilgilendirmiyor, hangi numarayı kastettiği ilgilendiriyor.
            var rakamlar = new StringBuilder(ham.Length);
            foreach (var k in ham)
            {
                if (char.IsAsciiDigit(k)) rakamlar.Append(k);
            }

            var s = rakamlar.ToString();

            // 2) Ülke kodu ve şehirlerarası sıfırı soy.
            //
            // ⚠️ Sıra önemli: önce en uzun önek. "00905321234567"
            //    içinde "90" öneki de var, "0" öneki de; kısa olandan
            //    başlasaydık geriye 905321234567 kalır ve bu 12 hane
            //    ikinci turda tekrar soyulmayı beklerdi.
            if (s.Length == 14 && s.StartsWith("0090")) s = s[4..];
            else if (s.Length == 13 && s.StartsWith("090")) s = s[3..];
            else if (s.Length == 12 && s.StartsWith("90")) s = s[2..];
            else if (s.Length == 11 && s.StartsWith("0")) s = s[1..];

            // 3) Geriye tam 10 hane kalmadıysa bu bir Türkiye
            //    numarası değil (ya da yazım hatası).
            //
            // ⚠️ Yurt dışı numaralarını da kabul eden gevşek bir
            //    kural yazmak cazipti ama o zaman "+1 555 0100" ile
            //    "5550100" ayrımını yapamazdık ve benzersizlik
            //    indeksi anlamını yitirirdi. Mağaza Türkiye'ye
            //    satış yapıyor; sınırı dürüstçe koyuyoruz.
            return s.Length == HaneSayisi ? s : null;
        }

        /// <summary>
        /// Kanonik numarayı ekranda okunacak hale getirir:
        /// "5528083129" → "0552 808 31 29"
        /// </summary>
        public static string Goster(string? numara)
        {
            if (string.IsNullOrWhiteSpace(numara)) return string.Empty;

            // ⚠️ Beklenmedik uzunlukta bir değer gelirse (geri
            // doldurulmuş eski bir kayıt olabilir) BİÇİMLENDİRMEYE
            // ZORLAMIYORUZ, olduğu gibi geri veriyoruz. Zorlasaydık
            // 19 haneli bozuk bir kayıt kırpılıp DOĞRU GÖRÜNEN ama
            // yanlış bir numaraya dönüşürdü — sessiz veri kaybı.
            if (numara.Length != HaneSayisi) return numara;

            return $"0{numara[..3]} {numara[3..6]} {numara[6..8]} {numara[8..]}";
        }
    }
}
