namespace ETicaretAPI.Services
{
    // Kurucuya verilen tek ürün satırı. Tutar KDV DAHİL ve indirim
    // uygulanmamış hali (UnitPrice × Quantity).
    public record IyzicoKalemGirdisi(
        int OrderItemId,
        string Ad,
        string Kategori,
        decimal SatirTutari);

    // Sonuç kalemi. Kargo satırında OrderItemId null olur.
    public record IyzicoSepetKalemi(
        string Id,
        string Ad,
        string Kategori,
        bool Fiziksel,
        decimal Tutar,
        int? OrderItemId);

    public record IyzicoSepetSonucu(
        decimal Price,
        List<IyzicoSepetKalemi> Kalemler);


    // ============================================================
    //  ⭐ YENİ — iyzico sepetini kuran SAF servis.
    //
    //  iyzico "price = sepet kalemlerinin toplamı" kuralını kuruşu
    //  kuruşuna dayatıyor; bizde ise kupon indirimi, kombin indirimi
    //  ve kargo var. iyzico negatif ya da sıfır kalem kabul etmiyor,
    //  yani indirim ayrı satır olarak eklenemiyor.
    //
    //  Çözüm: indirimi ürün kalemlerine tutarları oranında dağıt,
    //  kargoyu ayrı sanal kalem yap.
    //
    //  ⚠️ DbContext ALMIYOR. EF'e bağlı kod test edilemiyor
    //  (LogSorgusu.SayimiYorumla kararının aynısı) ve bu, projenin
    //  en çok test hak eden parçası.
    // ============================================================
    public class IyzicoSepetiKurucu
    {
        // iyzico'nun kabul ettiği en küçük kalem tutarı.
        private const decimal EnAzKalemTutari = 0.01m;

        // Kargo kaleminin sabit kimliği. İade eşleştirmesi buna bakıp
        // "bu satır ürün değil" diyebiliyor.
        public const string KargoKalemId = "kargo";

        public IyzicoSepetSonucu Kur(
            IReadOnlyList<IyzicoKalemGirdisi> kalemler,
            decimal kargoUcreti,
            decimal siparisToplami)
        {
            if (kalemler == null || kalemler.Count == 0)
            {
                throw new ArgumentException("Sepet boş.", nameof(kalemler));
            }

            if (kargoUcreti < 0)
            {
                throw new ArgumentException("Kargo ücreti negatif olamaz.", nameof(kargoUcreti));
            }

            // ⚠️ Hedef, girdilerden değil SİPARİŞ TOPLAMINDAN türetiliyor.
            // Böylece "kalemler toplamı = price" eşitliği hesabın
            // kurgusundan geliyor, girdilerin tutarlı olmasına
            // güvenmiyoruz.
            var urunHedefi = siparisToplami - kargoUcreti;

            var enAzGereken = EnAzKalemTutari * kalemler.Count;
            if (urunHedefi < enAzGereken)
            {
                throw new InvalidOperationException(
                    $"Sepet kurulamıyor: ürünlere kalan tutar {urunHedefi} TL, " +
                    $"{kalemler.Count} kalem için en az {enAzGereken} TL gerekiyor.");
            }

            var araToplam = kalemler.Sum(k => k.SatirTutari);
            if (araToplam <= 0)
            {
                throw new InvalidOperationException("Sepet ara toplamı sıfır ya da negatif.");
            }

            // 1) Oransal dağıtım. Kırpma, indirimin bir kalemi sıfıra
            // indirdiği durum için — iyzico 0 kabul etmiyor.
            var tutarlar = kalemler
                .Select(k => Math.Max(
                    EnAzKalemTutari,
                    Math.Round(k.SatirTutari * urunHedefi / araToplam, 2,
                               MidpointRounding.AwayFromZero)))
                .ToList();

            // 2) Yuvarlama artığını kapat. Yoksa toplam bir-iki kuruş
            // şaşar ve iyzico isteği tümden reddeder.
            ArtigiDagit(tutarlar, urunHedefi);

            var sonuc = new List<IyzicoSepetKalemi>();

            for (int i = 0; i < kalemler.Count; i++)
            {
                sonuc.Add(new IyzicoSepetKalemi(
                    Id: kalemler[i].OrderItemId.ToString(),
                    Ad: Kisalt(kalemler[i].Ad),
                    Kategori: Kisalt(kalemler[i].Kategori),
                    Fiziksel: true,
                    Tutar: tutarlar[i],
                    OrderItemId: kalemler[i].OrderItemId));
            }

            // 3) Kargo ayrı sanal kalem. Ücretsizse hiç eklenmiyor —
            // 0 TL'lik kalem iyzico'da geçersiz.
            if (kargoUcreti > 0)
            {
                sonuc.Add(new IyzicoSepetKalemi(
                    Id: KargoKalemId,
                    Ad: "Kargo",
                    Kategori: "Kargo",
                    Fiziksel: false,
                    Tutar: kargoUcreti,
                    OrderItemId: null));
            }

            return new IyzicoSepetSonucu(sonuc.Sum(k => k.Tutar), sonuc);
        }


        // Listeyi hedefe tam oturtur. Fazlaysa en büyük kalemlerden
        // düşer (en çok yeri olan orada), eksikse en büyüğe ekler.
        private static void ArtigiDagit(List<decimal> tutarlar, decimal hedef)
        {
            var fark = hedef - tutarlar.Sum();

            if (fark == 0)
            {
                return;
            }

            if (fark > 0)
            {
                var enBuyuk = EnBuyugunIndeksi(tutarlar);
                tutarlar[enBuyuk] += fark;
                return;
            }

            // Eksiltme: her kalem 0.01'e kadar verebilir. Toplam yer
            // yukarıdaki enAzGereken kontrolü sayesinde her zaman yeterli.
            var kalan = -fark;
            var sira = Enumerable.Range(0, tutarlar.Count)
                .OrderByDescending(i => tutarlar[i])
                .ToList();

            foreach (var i in sira)
            {
                if (kalan <= 0)
                {
                    break;
                }

                var verebilir = tutarlar[i] - EnAzKalemTutari;
                var alinan = Math.Min(verebilir, kalan);

                tutarlar[i] -= alinan;
                kalan -= alinan;
            }

            if (kalan > 0)
            {
                throw new InvalidOperationException(
                    "Sepet hedefe indirilemedi; kalemler alt sınıra dayandı.");
            }
        }

        private static int EnBuyugunIndeksi(List<decimal> tutarlar)
        {
            var indeks = 0;

            for (int i = 1; i < tutarlar.Count; i++)
            {
                if (tutarlar[i] > tutarlar[indeks])
                {
                    indeks = i;
                }
            }

            return indeks;
        }

        // iyzico alan uzunluklarına takılmamak için. Boş ad da kabul
        // edilmiyor, o yüzden yedek metin var.
        private static string Kisalt(string? metin)
        {
            if (string.IsNullOrWhiteSpace(metin))
            {
                return "Urun";
            }

            var temiz = metin.Trim();
            return temiz.Length <= 200 ? temiz : temiz[..200];
        }
    }
}
