namespace ETicaretAPI.Services
{
    // ============================================================
    //  MAĞAZA AYARLARI — TEK DOĞRU KAYNAK
    //
    //  appsettings.json'daki "Magaza" bölümünü tipli ve güvenli
    //  şekilde okur. Backend'in her yeri buradan okuyor.
    //
    //  ⚠️ NEDEN HER YERDE _config["Magaza:StokAzEsigi"] YAZMIYORUZ?
    //
    //  Üç sebep:
    //    1) YAZIM HATASI SESSİZDİR. "Magaza:StokAzEsigi" yerine
    //       "Magaza:StokAzEsik" yazarsan hiçbir hata almazsın —
    //       null döner, sen de 0 varsayarsın ve TÜM ürünler
    //       "az stoklu" görünür. Burada tek yerde yazılı,
    //       yanlışsa hepsi birden yanlış olur ve hemen fark edilir.
    //    2) TİP DÖNÜŞÜMÜ TEK YERDE. Config her şeyi string tutar.
    //       decimal'e çevirmeyi her çağrı yerinde tekrarlamak
    //       gerekirdi.
    //    3) DOĞRULAMA TEK YERDE. Negatif kargo ücreti veya sıfır
    //       stok eşiği yazan birine karşı korunuyoruz.
    //
    //  NEDEN IOptions<T> KULLANMADIK?
    //  .NET'in Options deseni de doğru bir seçim olurdu. Ama bu
    //  projede zaten RaporTarihi servisi IConfiguration'ı doğrudan
    //  okuyan bir sınıf olarak yazılmış. Aynı işi iki farklı
    //  desenle yapmak, okuyanı "acaba neden farklı?" diye
    //  durduruyor. Tutarlılık, buradaki küçük tip avantajından
    //  değerli.
    //
    //  NEDEN => (İFADE GÖVDELİ ÖZELLİK), CONSTRUCTOR'DA OKUMA YOK?
    //  Her erişimde config'e bakıyor. Bir kez okuyup alanda
    //  saklasaydık, appsettings çalışırken değiştirildiğinde
    //  (ASP.NET bunu destekler) eski değerler kullanılmaya devam
    //  ederdi. Config okuma bellek içi bir sözlük araması —
    //  maliyeti yok denecek kadar az.
    // ============================================================
    public class MagazaAyarlari
    {
        private readonly IConfiguration _config;

        public MagazaAyarlari(IConfiguration config)
        {
            _config = config;
        }

        // ---------- MAĞAZA KİMLİĞİ ----------

        public string Ad =>
            _config["Magaza:Ad"] ?? "Mağaza";

        public string Telefon =>
            _config["Magaza:Telefon"] ?? "";

        public string Adres =>
            _config["Magaza:Adres"] ?? "";

        // ---------- İŞ KURALLARI ----------

        // Stok bu sayının ALTINDA ise "az" sayılır.
        //
        // En az 1: sıfır olsaydı hiçbir ürün asla "az" olmazdı ve
        // kritik stok uyarıları sessizce çalışmaz hale gelirdi.
        // Sessizce çalışmayan bir uyarı, hiç olmayan uyarıdan
        // tehlikelidir — çünkü var sanırsın.
        public int StokAzEsigi =>
            TamSayi("StokAzEsigi", varsayilan: 5, enAz: 1, enFazla: 1000);

        public int SepetMaksAdet =>
            TamSayi("SepetMaksAdet", varsayilan: 99, enAz: 1, enFazla: 10000);

        // ⭐ YENİ (Aşama 9) — İADE (CAYMA) SÜRESİ, GÜN.
        //
        // Teslimattan sonra kaç gün içinde iade talebi açılabilir?
        //
        // ⚠️ Varsayılan 14 KEYFİ DEĞİL: mesafeli satış mevzuatındaki
        // cayma hakkı süresi. Aşama 10'da yazılacak sözleşme metni de
        // aynı sayıyı söyleyecek — iki yerde iki farklı gün yazması,
        // müşteriye sözleşmede vaat edileni sistemde vermemek olurdu.
        //
        // ⚠️ En az 7: yasal alt sınırın altına inmek mevzuata aykırı
        // olurdu ve bir yapılandırma hatası bunu sessizce yapabilirdi.
        // Ayar dosyasına 0 yazıp iadeyi tamamen kapatmak, kapatılması
        // gereken bir hakkı kapatmak demekti.
        public int IadeGunSayisi =>
            TamSayi("IadeGunSayisi", varsayilan: 14, enAz: 7, enFazla: 365);

        public string SiparisNoOneki =>
            _config["Magaza:SiparisNoOneki"] ?? "SP";

        // ---------- PARA (4.2'de kullanılacak) ----------

        public decimal KargoUcreti =>
            Ondalik("KargoUcreti", varsayilan: 0m, enAz: 0m);

        // 0 yazılırsa "her sipariş kargo bedava" anlamına gelir.
        // Bu geçerli bir mağaza politikası, engellemiyoruz.
        public decimal UcretsizKargoLimiti =>
            Ondalik("UcretsizKargoLimiti", varsayilan: 0m, enAz: 0m);

        // ---------- KDV ----------

        // Türkiye'de yürürlükteki oranlar: %1, %10, %20.
        // 0-100 aralığı dışına çıkmasını engelliyoruz.
        public int KdvVarsayilanOran =>
            TamSayi("KdvVarsayilanOran", varsayilan: 20, enAz: 0, enFazla: 100);

        // ⭐ YENİ — KARGO HİZMETİNİN KDV ORANI
        //
        // Kargo bir hizmettir ve KDV'ye tabidir. KargoUcreti de tıpkı
        // ürün fiyatları gibi KDV DAHİL bir tutar; bu oran onun üstüne
        // eklenmez, içinden ayrıştırılır.
        //
        // ⚠️ NEDEN KdvVarsayilanOran'DAN AYRI?
        //
        // İkisi aynı sayı (20) olsa da FARKLI SORULARIN cevabı:
        //   KdvVarsayilanOran → "yeni eklenen ÜRÜN hangi orana düşsün"
        //   KargoKdvOrani     → "KARGO hizmeti hangi orana tabi"
        //
        // Ürün varsayılanı mağazanın ne sattığına göre değişir (gıda
        // satan bir mağaza bunu 1 yapar). Kargonun oranı ise mağazanın
        // ne sattığından bağımsızdır. Tek ayara bağlasaydık, gıda
        // mağazası ürün varsayılanını 1'e çektiğinde kargo da sessizce
        // %1'e düşerdi — ve bu vergi hatası olurdu.
        //
        // "Aynı değere sahip olmak, aynı şey olmak demek değildir."
        public int KargoKdvOrani =>
            TamSayi("KargoKdvOrani", varsayilan: 20, enAz: 0, enFazla: 100);


        // ==========================================================
        //  ÖZEL YARDIMCILAR
        // ==========================================================

        // Tam sayı ayarı oku, sınırların dışındaysa kırp.
        //
        // ⚠️ NEDEN AYARI DOĞRULUYORUZ?
        //
        // appsettings.json'a "StokAzEsigi": -5 yazan biri olabilir
        // (parmak kayması, kopyala-yapıştır). Doğrulamasaydık:
        // hiçbir hata alınmaz, sadece stok uyarıları sessizce
        // çalışmaz olurdu. Bunu aylar sonra, stok bitince fark
        // ederdik.
        //
        // "Yapılandırma da bir girdidir" — kullanıcı girdisi gibi
        // doğrulanır. Fark şu: kullanıcıya hata mesajı gösteririz,
        // yapılandırmada güvenli varsayılana düşeriz. Çünkü sunucu
        // ayar hatası yüzünden hiç açılmamaktansa makul değerlerle
        // çalışmalı.
        private int TamSayi(string anahtar, int varsayilan, int enAz, int enFazla)
        {
            // GetValue<int?> — nullable soruyoruz ki "0 yazılmış"
            // ile "hiç yazılmamış" durumlarını ayırt edebilelim.
            // GetValue<int> olsaydı ikisi de 0 dönerdi.
            var deger = _config.GetValue<int?>("Magaza:" + anahtar);

            if (deger == null)
            {
                return varsayilan;
            }

            if (deger < enAz)
            {
                return enAz;
            }

            if (deger > enFazla)
            {
                return enFazla;
            }

            return deger.Value;
        }

        // Ondalık ayar oku (para değerleri için).
        //
        // decimal kullanıyoruz, double DEĞİL. Para hesabında double
        // kayan nokta hatası yapar: 0.1 + 0.2 = 0.30000000000000004.
        // Projedeki tüm para alanları decimal — tutarlıyız.
        private decimal Ondalik(string anahtar, decimal varsayilan, decimal enAz)
        {
            var deger = _config.GetValue<decimal?>("Magaza:" + anahtar);

            if (deger == null)
            {
                return varsayilan;
            }

            if (deger < enAz)
            {
                return enAz;
            }

            return deger.Value;
        }
    }
}