namespace ETicaretAPI.Services
{
    // ⭐ YENİ — LOG SAKLAMA AYARLARI
    //
    // appsettings'teki "Loglar" bölümünü tipli okur. Süreler koda
    // gömülmüyor: 6 ay yetmediğinde çevrilecek düğme bu sayının kendisi.
    //
    // MagazaAyarlari ile aynı desen ve aynı gerekçe — yazım hatası tek
    // yerde, tip dönüşümü tek yerde, doğrulama tek yerde.
    public class LogAyarlari
    {
        private readonly IConfiguration _config;

        public LogAyarlari(IConfiguration config)
        {
            _config = config;
        }

        // ⚠️ En az 1 gün: 0 yazan biri bugünün kayıtlarını da sildirirdi
        // ve denetim kaydı fiilen kapanırdı.
        public int DenetimGun => Gun("DenetimGun", varsayilan: 180);
        public int EmailGun => Gun("EmailGun", varsayilan: 30);
        public int GirisGun => Gun("GirisGun", varsayilan: 30);
        public int HataGun => Gun("HataGun", varsayilan: 30);

        // ⚠️ Üst sınır 100.000: daha büyüğü tek turda uzun kilit tutar.
        public int TemizlikPartiBoyutu
        {
            get
            {
                var deger = _config.GetValue<int?>("Loglar:TemizlikPartiBoyutu") ?? 10000;
                return Math.Clamp(deger, 100, 100000);
            }
        }

        private int Gun(string anahtar, int varsayilan)
        {
            var deger = _config.GetValue<int?>("Loglar:" + anahtar);

            if (deger == null)
            {
                return varsayilan;
            }

            return Math.Clamp(deger.Value, 1, 3650);
        }
    }
}
