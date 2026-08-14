namespace ETicaretAPI.Models
{
    // ⭐ YENİ — GİRİŞ DENEMESİ KAYDI
    //
    // Bugün yalnızca sayaç vardı (User.YanlisGirisSayisi) ve o sayaç
    // başarılı girişte sıfırlanıyor: "bu hesaba dün gece 40 kez denendi"
    // sorusu cevaplanamıyordu.
    //
    // ⚠️ Yalnızca süperadmine açık. E-posta yazılıyor ama hiçbir müşteri
    // ucu bu tabloyu okumuyor — aksi hâlde "hesap var mı" bilgisi sızardı.
    public class GirisKaydi
    {
        public int Id { get; set; }

        // ⚠️ ŞİFRE HİÇBİR KOŞULDA YAZILMIYOR — yanlış girilen bile.
        // Yanlış şifre çoğu zaman kullanıcının BAŞKA bir hesaptaki
        // doğru şifresidir.
        public string Email { get; set; } = string.Empty;

        // GirisSonucu sabitlerinden biri.
        public string Sonuc { get; set; } = string.Empty;

        // Vekil zinciri çözülmüş istemci adresi; bilinmiyorsa null.
        public string? IpAdresi { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }


    // Elle yazılan metin yerine sabit: "sifre_yanlis" yerine
    // "sifre_yanlıs" yazmak hata vermez, sadece filtreden kaçardı.
    public static class GirisSonucu
    {
        public const string Basarili = "basarili";
        public const string SifreYanlis = "sifre_yanlis";
        public const string KullaniciYok = "kullanici_yok";
        public const string HesapKilitli = "hesap_kilitli";
        public const string HesapPasif = "hesap_pasif";
        public const string Dogrulanmamis = "dogrulanmamis";
    }
}
