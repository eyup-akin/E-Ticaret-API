namespace ETicaretAPI.Services
{
    // ⭐ YENİ (B2) — KAMPANYADAKİ SATIR LİSTELERİ
    //
    // Kupon kodları ve koşullar veritabanında satır satır tek kolonda
    // duruyor (gerekçesi Kampanya modelinde), dışarıya ise dizi olarak
    // çıkıyor. Dönüşüm iki controller'da da lazım: müşteri ucu okuyor,
    // admin ucu hem okuyor hem yazıyor.
    //
    // ⚠️ Üç yerde tekrarlanacaktı ve tekrar sessizce ayrışırdı: biri
    // boş satırları eleyip diğeri elemeseydi ekranda boş bir madde
    // işareti belirirdi — hiçbir yerde hata vermeden.
    public static class KampanyaSatirlari
    {
        // ⚠️ Windows'tan yapıştırılan metin \r\n ile geliyor, tarayıcı
        // textarea'sı \n ile. İkisini de bölüyoruz; yoksa satır
        // sonlarında görünmez \r kalır ve kupon kodu "SUPER50\r"
        // olduğu için sunucuda bulunamazdı.
        private static readonly string[] Ayraclar = { "\r\n", "\n", "\r" };

        public static List<string> Bol(string? metin)
        {
            if (string.IsNullOrWhiteSpace(metin))
            {
                return new List<string>();
            }

            return metin
                .Split(Ayraclar, StringSplitOptions.None)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }

        public static string Birlestir(IEnumerable<string>? satirlar)
        {
            if (satirlar == null)
            {
                return string.Empty;
            }

            return string.Join(
                "\n",
                satirlar.Select(s => (s ?? string.Empty).Trim()).Where(s => s.Length > 0));
        }
    }
}
