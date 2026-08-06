namespace ETicaretAPI.Services
{
    // Tek bir KDV oranına ait ayrıştırma sonucu.
    //
    // Neden ayrı sınıf, neden tuple değil?
    // SepetOzeti'ndeki gerekçenin aynısı: üç değer dönüyor ve
    // isimleri anlam taşıyor. "Item2 matrah mıydı vergi miydi?"
    // sorusunu kimse sormamalı.
    public class KdvSatiri
    {
        // Yüzde olarak oran (1, 10, 20)
        public int Oran { get; set; }

        // KDV hariç tutar — verginin hesaplandığı taban
        public decimal Matrah { get; set; }

        // Verginin kendisi
        public decimal Vergi { get; set; }

        // KDV dahil tutar (Matrah + Vergi)
        //
        // Türetilebilir bir değer ama saklıyoruz: çağıran taraf
        // "girdiğim tutar buydu" diye doğrulama yapabilsin.
        // Denetim kaydındaki OncekiStok/SonrakiStok ile aynı fikir —
        // fazlalık israf değil, doğrulama aracı.
        public decimal DahilTutar { get; set; }
    }


    // Bir siparişin tamamının KDV dökümü.
    public class KdvOzeti
    {
        // Oran bazında kırılım, orana göre artan sırada.
        //
        // ⚠️ NEDEN TEK BİR ORAN DEĞİL, LİSTE?
        //
        // Bir sepette %1'lik gıda ile %20'lik elektronik aynı anda
        // olabilir. "Siparişin KDV oranı" diye tek bir şey yok.
        // Tek oran döndürseydik ya yanlış olurdu ya da ortalama
        // almak zorunda kalırdık — ve ortalama KDV oranı diye bir
        // kavram muhasebede yoktur, fatura satır satır kesilir.
        public List<KdvSatiri> Satirlar { get; set; } = new List<KdvSatiri>();

        // Tüm satırların matrah toplamı
        public decimal ToplamMatrah { get; set; }

        // Tüm satırların vergi toplamı
        public decimal ToplamVergi { get; set; }

        // Döküm gösterilebilir mi?
        //
        // ⚠️ BU ALAN NEDEN VAR?
        //
        // KDV oranları siparişe DONDURULUYOR ve bu alan eklenmeden
        // önceki siparişlerde null. O siparişlerde hangi oranın
        // uygulandığını bilmiyoruz.
        //
        // Ekranların "Satirlar.Count > 0 mu?" diye kendi kararını
        // vermesi yerine buradan söylüyoruz. Üç ekran aynı kontrolü
        // ayrı ayrı yazsaydı, biri yanlış yazdığında eski siparişte
        // "KDV: 0,00 TL" görünürdü — yani YANLIŞ bilgi.
        public bool DokumVarMi { get; set; }
    }


    // ============================================================
    //  KDV AYRIŞTIRMA — TEK DOĞRU KAYNAK
    //
    //  ⚠️ FİYATLAR KDV DAHİLDİR.
    //
    //  Bu servis vergi EKLEMEZ, içinden ÇIKARIR:
    //
    //      Matrah = Tutar × 100 / (100 + Oran)
    //      Vergi  = Tutar − Matrah
    //
    //  Türkiye'de tüketiciye gösterilen fiyatın tüm vergiler dahil
    //  nihai fiyat olması zorunlu. Fiyatın üstüne eklemek hem yasaya
    //  aykırı olurdu hem de sepette gösterilen tutar ile ödenen
    //  tutarı ayrıştırırdı.
    //
    //  ⚠️ NEDEN AYRI BİR SERVİS?
    //
    //  Aynı ayrıştırma ÜÇ yerde gösterilecek:
    //    1) Admin sipariş detayı
    //    2) Mobil sipariş detayı
    //    3) İleride: fatura / mesafeli satış sözleşmesi (Aşama 10)
    //
    //  Formül tek satır görünüyor ama iki tuzağı var: yuvarlamanın
    //  NEREDE yapılacağı ve birden fazla oranın nasıl gruplanacağı.
    //  Üç yerde ayrı yazılsaydı biri satır bazında, diğeri toplam
    //  bazında yuvarlar ve iki ekran birbirini kuruş tutmazdı.
    //
    //  SepetHesaplayici'yi tam da bu sebeple yazmıştık.
    //
    //  ⚠️ TOPLAMI DEĞİŞTİRMEZ.
    //
    //  Bu servis hiçbir zaman "ödenecek tutar"ı üretmez. Fiyat zaten
    //  KDV dahil olduğu için toplam aynı kalır; burada üretilen tek
    //  şey o toplamın DÖKÜMÜ. Toplamı üreten tek yer
    //  SepetHesaplayici'dir ve öyle kalmalı.
    //
    //  ⚠️ NEDEN VERİTABANINA DOKUNMUYOR?
    //  Saf hesap: girdi al, sonuç ver. DbContext almadığı için
    //  Singleton olabiliyor ve test edilmesi kolay.
    // ============================================================
    public class KdvHesaplayici
    {
        // Tek bir tutarı ayrıştırır.
        //
        // tutar : KDV DAHİL tutar
        // oran  : yüzde (1, 10, 20)
        public KdvSatiri Ayristir(decimal tutar, int oran)
        {
            // ---------- SAVUNMACI DÜZELTMELER ----------
            //
            // SepetHesaplayici'deki yaklaşımın aynısı: bu servis
            // birden fazla yerden çağrılıyor, birinde hata olursa
            // patlamak yerine makul davranıyoruz.
            if (tutar < 0)
            {
                tutar = 0;
            }

            // ⚠️ NEGATİF ORAN SIFIRA ÇEKİLİR, İSTİSNA FIRLATILMAZ.
            //
            // Oran doğrulaması DTO katmanının işi (beyaz liste).
            // Burada fırlatsaydık, tek bozuk ürün yüzünden sipariş
            // detay sayfası hiç açılmazdı — müşteri siparişini
            // göremezdi. Bozuk oranda KDV'yi 0 göstermek, sayfayı
            // hiç göstermemekten iyidir.
            if (oran < 0)
            {
                oran = 0;
            }

            // ---------- AYRIŞTIRMA ----------
            //
            // ⚠️ MATRAHI ÖNCE HESAPLAYIP VERGİYİ ÇIKARMA İLE BULUYORUZ.
            //
            // Alternatif şuydu:
            //     vergi  = tutar × oran / (100 + oran)
            //     matrah = tutar − vergi
            //
            // İkisi matematiksel olarak aynı ama yuvarlamadan sonra
            // AYNI DEĞİL. Hangisini seçersek seçelim, önemli olan
            // "matrah + vergi = tutar" eşitliğinin BOZULMAMASI.
            //
            // Bu yüzden ikisinden birini hesaplayıp diğerini
            // ÇIKARMA ile buluyoruz. İkisini de ayrı ayrı
            // yuvarlasaydık toplamları 1 kuruş sapabilir ve ekranda
            // "1.000,00 + 200,00 = 1.200,01" gibi bir satır çıkardı.
            var matrah = Yuvarla(tutar * 100m / (100m + oran));

            // Çıkarma ile bulunuyor — eşitlik garanti.
            var vergi = tutar - matrah;

            return new KdvSatiri
            {
                Oran = oran,
                Matrah = matrah,
                Vergi = vergi,
                DahilTutar = tutar
            };
        }


        // Birden fazla kalemi oran bazında gruplayıp döküm üretir.
        //
        // kalemler : (tutar, oran) çiftleri. Oran null olan kalemler
        //            ATLANIR — oranı bilinmeyen eski siparişler.
        public KdvOzeti Ozetle(IEnumerable<(decimal Tutar, int? Oran)> kalemler)
        {
            var ozet = new KdvOzeti();

            // ⚠️ ORANI BİLİNMEYEN KALEMLER DÖKÜME GİRMEZ.
            //
            // OrderItem.VatRate ve Order.ShippingVatRate, bu özellik
            // eklenmeden önceki kayıtlarda null. O siparişlerde hangi
            // oranın uygulandığını gerçekten bilmiyoruz.
            //
            // 0 sayıp döküme katsaydık "bu kalemde KDV alınmadı" diye
            // YANLIŞ bir iddiada bulunurduk. Atlıyoruz ve aşağıda
            // DokumVarMi = false diyerek ekranın satırı hiç
            // çizmemesini sağlıyoruz.
            var bilinenler = kalemler.Where(k => k.Oran != null);

            // Oran bazında grupla.
            //
            // ⚠️ NEDEN ÖNCE GRUPLAYIP SONRA AYRIŞTIRIYORUZ?
            //
            // Kalem kalem ayrıştırıp sonra toplasaydık, her kalemde
            // ayrı bir yuvarlama olurdu ve 10 kalemlik bir siparişte
            // 10 kuruşa kadar sapma birikebilirdi.
            //
            // Aynı orandaki tutarları önce toplayıp TEK SEFER
            // ayrıştırınca yuvarlama bir kez yapılıyor. Fatura
            // mantığı da böyledir: KDV oran bazında hesaplanır,
            // satır bazında değil.
            var gruplar = bilinenler
                .GroupBy(k => k.Oran!.Value)
                .OrderBy(g => g.Key);

            foreach (var grup in gruplar)
            {
                var grupTutari = grup.Sum(k => k.Tutar);
                var satir = Ayristir(grupTutari, grup.Key);

                ozet.Satirlar.Add(satir);
                ozet.ToplamMatrah += satir.Matrah;
                ozet.ToplamVergi += satir.Vergi;
            }

            // Tek bir bilinen oran bile yoksa döküm gösterilmez.
            ozet.DokumVarMi = ozet.Satirlar.Count > 0;

            return ozet;
        }


        // AwayFromZero: 0,005 → 0,01.
        //
        // .NET'in varsayılanı "bankacı yuvarlaması"dır (0,005 → 0,00).
        // KuponServisi ve SepetHesaplayici'de de aynı seçimi yaptık —
        // üç servisin farklı yuvarlaması, aynı siparişte birbirini
        // tutmayan sayılar üretirdi.
        private static decimal Yuvarla(decimal deger)
        {
            return Math.Round(deger, 2, MidpointRounding.AwayFromZero);
        }
    }
}
