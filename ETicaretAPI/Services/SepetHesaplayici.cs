namespace ETicaretAPI.Services
{
    // Sepet hesabının SONUCU.
    //
    // Neden ayrı bir sınıf, neden tuple veya out parametre değil?
    // Dört değer dönüyor ve isimleri anlam taşıyor. Tuple olsaydı
    // çağıran taraf "Item3 neydi?" diye bakmak zorunda kalırdı.
    public class SepetOzeti
    {
        // Ürünlerin toplamı (indirim ve kargo hariç)
        public decimal AraToplam { get; set; }

        // Kupon indirimi (pozitif sayı, çıkarılacak)
        public decimal Indirim { get; set; }

        // Kargo ücreti — 0 ise ücretsiz kargo devrede
        public decimal KargoUcreti { get; set; }

        // Müşterinin ödeyeceği nihai tutar
        public decimal Toplam { get; set; }

        // Ücretsiz kargoya ne kadar kaldı?
        // 0 ise ya eşik aşılmış ya da ücretsiz kargo kapalı.
        // Mobil bunu "X TL daha ekle, kargo bedava" için kullanacak.
        public decimal UcretsizKargoyaKalan { get; set; }

        // Ücretsiz kargo bu siparişte geçerli mi?
        //
        // "KargoUcreti == 0" ile aynı şey DEĞİL: kargo ücreti
        // ayarda zaten 0 olabilir (mağaza kargoyu hiç almıyor).
        // Bu alan "eşiği geçtiğin İÇİN bedava" der ve mobil
        // "Kargo bedava! 🎉" rozetini buna göre gösterir.
        public bool UcretsizKargoKazanildi { get; set; }
    }


    // ============================================================
    //  SEPET HESABI — TEK DOĞRU KAYNAK
    //
    //  ⚠️ NEDEN AYRI BİR SERVİS?
    //
    //  Bu hesap şu an ÜÇ yerde yapılıyor:
    //    1) Mobil sepet ekranı   ("toplam ne olacak?")
    //    2) Mobil sipariş onayı  ("ne kadar ödeyeceğim?")
    //    3) OrdersController     (gerçek yazma)
    //
    //  Şimdiye kadar sorun çıkmadı çünkü formül tek satırdı:
    //  ürünler − indirim. Kargo eklenince formül DALLANIYOR:
    //  eşik kontrolü var, eşik indirimli tutara bakıyor, kargo
    //  indirimden sonra ekleniyor.
    //
    //  Üç yerde ayrı yazılsaydı biri güncellenip diğeri unutulurdu
    //  ve müşteri sepette 549 TL görüp onay ekranında 599 TL
    //  öderdi. KuponServisi'ni tam da bu sebeple yazmıştık.
    //
    //  ⚠️ HESAP SIRASI ÖNEMLİ:
    //
    //      Ara toplam (ürünler)
    //        − indirim (kupon)
    //        + kargo                ← indirimden SONRA
    //        = toplam
    //
    //  Kupon indirimi kargoya UYGULANMAZ. "%20 indirim" kuponu
    //  kargo ücretini de %20 indirseydi, mağaza kargo firmasına
    //  tam ödeyip müşteriden eksik alırdı.
    //
    //  ⚠️ NEDEN VERİTABANINA HİÇ DOKUNMUYOR?
    //
    //  Bu sınıf saf hesap yapıyor: girdi al, sonuç ver. DbContext
    //  almadığı için Singleton olabiliyor, test edilmesi kolay ve
    //  hangi bağlamdan çağrıldığı önemsiz.
    //
    //  Sepeti veritabanından çekmek ÇAĞIRANIN işi — çünkü
    //  OrdersController sepeti zaten transaction içinde ve kilit
    //  altında çekiyor, CouponsController ise serbestçe.
    // ============================================================
    public class SepetHesaplayici
    {
        private readonly MagazaAyarlari _ayarlar;

        public SepetHesaplayici(MagazaAyarlari ayarlar)
        {
            _ayarlar = ayarlar;
        }

        // araToplam : ürünlerin toplamı (indirim öncesi)
        // indirim   : kupon indirimi (yoksa 0)
        public SepetOzeti Hesapla(decimal araToplam, decimal indirim)
        {
            // ---------- SAVUNMACI DÜZELTMELER ----------
            //
            // Bu servis üç farklı yerden çağrılıyor; birinde bir
            // hata olursa burada patlamak yerine makul davranıyoruz.
            //
            // Negatif ara toplam matematiksel olarak imkânsız ama
            // savunmacı olmak bedava.
            if (araToplam < 0)
            {
                araToplam = 0;
            }

            if (indirim < 0)
            {
                indirim = 0;
            }

            // ⚠️ İndirim ara toplamı AŞAMAZ.
            //
            // KuponServisi bunu zaten kontrol ediyor ama burada da
            // yapıyoruz. Neden tekrar? Çünkü bu servis kupon
            // servisinden bağımsız çağrılabilir ve indirimin
            // ara toplamı aşması TOPLAMI EKSİYE düşürür —
            // yani müşteriye para vermiş oluruz.
            //
            // Para hesabında "bir yerde kontrol ediliyordur"
            // varsayımı yapılmaz.
            if (indirim > araToplam)
            {
                indirim = araToplam;
            }

            // ---------- İNDİRİMLİ TUTAR ----------
            //
            // Kargo eşiği hesabının dayanağı bu sayı.
            var indirimliTutar = araToplam - indirim;

            // ---------- KARGO ----------
            var kargoUcreti = _ayarlar.KargoUcreti;
            var limit = _ayarlar.UcretsizKargoLimiti;

            var ucretsizKazanildi = false;
            decimal kalan = 0;

            if (kargoUcreti <= 0)
            {
                // Mağaza kargo ücreti almıyor (ayar 0).
                //
                // ⚠️ Bu "ücretsiz kargo KAZANILDI" demek DEĞİL.
                // Müşteriye "tebrikler, kargo bedava!" demek
                // yanlış olurdu — zaten hiç ücretli değildi.
                // Ödül olmayan şeyi ödül gibi sunmuyoruz.
                kargoUcreti = 0;
            }
            else if (limit > 0 && indirimliTutar >= limit)
            {
                // ⚠️ EŞİK İNDİRİMLİ TUTARA BAKIYOR — karar buydu.
                //
                // Alternatifi ara toplama bakmaktı. O durumda:
                // sepet 600 TL, kupon 100 TL indirim yapıyor,
                // eşik 500 TL. Ara toplama baksaydık kargo bedava
                // olurdu (600 >= 500) ama müşteri aslında 500
                // ödüyor. Kupon uygulandığı anda kargo satırı
                // kaybolur, sonra başka bir şey olunca geri gelir —
                // müşteri neyin ne olduğunu anlayamaz.
                //
                // İndirimli tutara bakınca kural tek cümle:
                // "ÖDEDİĞİN tutar 500'ü geçerse kargo bedava."
                kargoUcreti = 0;
                ucretsizKazanildi = true;
            }
            else if (limit > 0)
            {
                // Eşiğe ne kadar kaldı?
                // Mobilde "42,50 TL daha ekle, kargo bedava" için.
                kalan = limit - indirimliTutar;
            }

            // ---------- SEPET BOŞSA KARGO DA YOK ----------
            //
            // ⚠️ Bu kontrol olmadan boş sepette toplam 49,90 çıkardı.
            //
            // Sipariş akışı boş sepeti zaten reddediyor ama sepet
            // ekranı sepet boşken de bu hesabı çağırıyor. Müşteriye
            // boş sepette 49,90 TL göstermek saçma olurdu.
            if (araToplam <= 0)
            {
                kargoUcreti = 0;
                ucretsizKazanildi = false;
                kalan = 0;
            }

            // ---------- TOPLAM ----------
            var toplam = indirimliTutar + kargoUcreti;

            return new SepetOzeti
            {
                // ⚠️ HER PARA DEĞERİ KURUŞA YUVARLANIYOR.
                //
                // Yüzdelik kupon hesabı 149,999999... gibi değerler
                // üretebiliyor. Yuvarlamasaydık veritabanına
                // decimal(18,2) yazılırken SQL Server kendi
                // yuvarlamasını yapardı ve bizim gösterdiğimiz
                // sayı ile kaydedilen sayı farklı olabilirdi.
                //
                // AwayFromZero: 0,005 → 0,01. .NET'in varsayılanı
                // "bankacı yuvarlaması"dır (0,005 → 0,00) ve
                // müşteri lehine değildir. KuponServisi'nde de
                // aynı seçimi yaptık.
                AraToplam = Yuvarla(araToplam),
                Indirim = Yuvarla(indirim),
                KargoUcreti = Yuvarla(kargoUcreti),
                Toplam = Yuvarla(toplam),
                UcretsizKargoyaKalan = Yuvarla(kalan),
                UcretsizKargoKazanildi = ucretsizKazanildi
            };
        }

        private static decimal Yuvarla(decimal deger)
        {
            return Math.Round(deger, 2, MidpointRounding.AwayFromZero);
        }
    }
}