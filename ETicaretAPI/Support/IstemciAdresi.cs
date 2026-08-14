namespace ETicaretAPI.Support
{
    // ⭐ YENİ — GERÇEK İSTEMCİ IP'Sİ, TEK YERDEN
    //
    // ⚠️ NEDEN AYRI BİR YARDIMCI?
    //
    // IP okuma üç yerde ayrı ayrı yazılıydı (sözleşme onayı, sipariş
    // onayı) ve şimdi dört yer daha ekleniyor (denetim, giriş, hata).
    // Vekil kuralı bir gün değişirse hepsinin birden değişmesi gerekir;
    // tek yerde unutulan biri, TÜM kayıtlarında vekilin adresini yazar
    // — hiç IP tutmamaktan beter, çünkü yanlış bilgi doğru sanılır.
    //
    // ⚠️ Connection.RemoteIpAddress'i doğrudan okumak burada DOĞRU:
    // Program.cs'teki UseForwardedHeaders vekil zincirini çözüp bu
    // alanın ÜSTÜNE yazıyor (Vekil:GuvenilirAglar doluyken). Yani
    // "vekil zincirini çözen ortak yer" burası; X-Forwarded-For
    // başlığını burada elle ayrıştırmak, güvenilir ağ kontrolünü
    // atlamak olurdu.
    public static class IstemciAdresi
    {
        /// <summary>
        /// İstemcinin adresi; bilinemiyorsa null.
        /// </summary>
        public static string? Oku(HttpContext? context)
        {
            var adres = context?.Connection.RemoteIpAddress;

            if (adres == null)
            {
                return null;
            }

            // ⚠️ IPv4-eşlenmiş IPv6 (::ffff:192.168.1.1) sadeleştiriliyor.
            // Aksi hâlde aynı istemci iki farklı biçimde kaydedilir ve
            // ekranda arayan kişi ikisini farklı sanır.
            if (adres.IsIPv4MappedToIPv6)
            {
                adres = adres.MapToIPv4();
            }

            return adres.ToString();
        }
    }
}
