namespace ETicaretAPI.Support
{
    // ⭐ YENİ — SAYFA NUMARASININ SINIRI
    //
    // ⚠️ Bu bir TAŞMA koruması. `Skip((page - 1) * pageSize)` int
    // aritmetiği: page=2000000000 çarpımı taşıp NEGATİFE dönüyor ve
    // SQL Server "OFFSET may not be negative" deyip 500 fırlatıyor.
    //
    // ⚠️ pageSize'a sınır koymak yetmiyordu — çarpımın diğer yanı
    // page ve dokuz uçta onun sınırı yoktu.
    public static class SayfaSiniri
    {
        // 100'lük sayfalarla 10 milyon satır: gerçek kullanım buraya
        // gelmez, çarpım int'e rahat sığar.
        public const int EnBuyukSayfa = 100000;

        /// <summary>Sayfa numarasını güvenli aralığa çeker.</summary>
        //
        // ⚠️ pageSize burada YOK: her ucun kendi varsayılanı var
        // (kullanıcı listesi 10, iade 20) ve bu bilinçli.
        public static int Duzelt(int page)
        {
            return Math.Clamp(page, 1, EnBuyukSayfa);
        }
    }
}
