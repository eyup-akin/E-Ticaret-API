using System.Globalization;

namespace ETicaretAPI.Support
{
    // ⭐ YENİ — AD MASKELEME (hesap kapatma / KVKK)
    //
    // "Eyüp Akın" → "E***"
    public static class Maskeleme
    {
        // ⚠️ TÜRKÇE BÜYÜK HARF TUZAĞI.
        //
        // Varsayılan kültürde "irem".ToUpper() → "IREM" veriyor ve baş
        // harf "I" oluyor; doğrusu "İ". Aynı hata mobildeki avatar
        // harfinde de yaşanmıştı. tr-TR kültürü açıkça veriliyor —
        // sunucunun kültürüne güvenmek, Docker'da (invariant culture)
        // sessizce yanlış sonuç üretirdi.
        private static readonly CultureInfo Turkce = new("tr-TR");

        /// <summary>
        /// Adın ilk harfini bırakır, kalanını yıldızlar. Ad boşsa "***".
        /// </summary>
        public static string Ad(string? ad)
        {
            // ⚠️ Boş kontrolü şart: null.Substring(0, 1) patlar ve
            // hesap kapatma akışını yarıda bırakırdı.
            if (string.IsNullOrWhiteSpace(ad))
            {
                return "***";
            }

            var temiz = ad.Trim();

            return temiz[..1].ToUpper(Turkce) + "***";
        }
    }
}
