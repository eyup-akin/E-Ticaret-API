namespace ETicaretAPI.Tests
{
    // SepetHesaplayici — sepet toplamının TEK DOĞRU KAYNAĞI.
    //
    // Bu servisi üç yer çağırıyor (sepet ekranı, kupon önizleme, sipariş
    // oluşturma) ve üçünün aynı sonucu vermesi müşterinin sepette
    // gördüğü tutarı ödemesi demek. Buradaki bir hata doğrudan para
    // hatasıdır — testin asıl gerekçesi bu.
    public class SepetHesaplayiciTestleri
    {
        // ---------- KARGO EŞİĞİ ----------

        [Fact]
        public void Esik_Altinda_Kargo_Ucreti_Alinir()
        {
            var hesaplayici = AyarYardimcisi.Hesaplayici(
                kargoUcreti: 49.90m, ucretsizKargoLimiti: 500m);

            var ozet = hesaplayici.Hesapla(araToplam: 300m, indirim: 0m);

            Assert.Equal(49.90m, ozet.KargoUcreti);
            Assert.Equal(349.90m, ozet.Toplam);
            Assert.False(ozet.UcretsizKargoKazanildi);
            Assert.Equal(200m, ozet.UcretsizKargoyaKalan);
        }

        [Fact]
        public void Esik_Tam_Ustundeyken_Kargo_Ucretsiz()
        {
            // ⚠️ SINIR DEĞERİ: kural ">=" olduğu için tam eşik de bedava.
            // ">" olsaydı 500 TL'lik sepette kargo çıkardı ve müşteri
            // "500'ü geçince bedava" yazısına rağmen ücret görürdü.
            var hesaplayici = AyarYardimcisi.Hesaplayici(
                kargoUcreti: 49.90m, ucretsizKargoLimiti: 500m);

            var ozet = hesaplayici.Hesapla(araToplam: 500m, indirim: 0m);

            Assert.Equal(0m, ozet.KargoUcreti);
            Assert.Equal(500m, ozet.Toplam);
            Assert.True(ozet.UcretsizKargoKazanildi);
            Assert.Equal(0m, ozet.UcretsizKargoyaKalan);
        }

        [Fact]
        public void Esik_Indirimli_Tutara_Gore_Degerlendirilir()
        {
            // ⚠️ EN ÖNEMLİ TEST.
            //
            // Sepet 600, kupon 150 indiriyor, eşik 500.
            // Ara toplama bakılsaydı (600 >= 500) kargo bedava olurdu —
            // oysa müşteri 450 ödüyor. Kural "ÖDEDİĞİN tutar 500'ü
            // geçerse bedava".
            var hesaplayici = AyarYardimcisi.Hesaplayici(
                kargoUcreti: 49.90m, ucretsizKargoLimiti: 500m);

            var ozet = hesaplayici.Hesapla(araToplam: 600m, indirim: 150m);

            Assert.Equal(49.90m, ozet.KargoUcreti);
            Assert.False(ozet.UcretsizKargoKazanildi);
            Assert.Equal(499.90m, ozet.Toplam);   // 600 − 150 + 49,90
        }

        [Fact]
        public void Kupon_Indirimi_Kargoya_Uygulanmaz()
        {
            // Kargo indirimden SONRA ekleniyor. Aksi halde "%20 indirim"
            // kuponu kargo ücretini de indirir, mağaza kargo firmasına
            // tam ödeyip müşteriden eksik alırdı.
            var hesaplayici = AyarYardimcisi.Hesaplayici(
                kargoUcreti: 50m, ucretsizKargoLimiti: 1000m);

            var ozet = hesaplayici.Hesapla(araToplam: 100m, indirim: 20m);

            Assert.Equal(50m, ozet.KargoUcreti);   // 40 değil
            Assert.Equal(130m, ozet.Toplam);
        }

        // ---------- SAVUNMACI DAVRANIŞ ----------

        [Fact]
        public void Bos_Sepette_Kargo_Alinmaz()
        {
            // Sipariş akışı boş sepeti zaten reddediyor ama sepet ekranı
            // bu hesabı sepet boşken de çağırıyor. Müşteriye boş sepette
            // 49,90 TL göstermek saçma olurdu.
            var hesaplayici = AyarYardimcisi.Hesaplayici();

            var ozet = hesaplayici.Hesapla(araToplam: 0m, indirim: 0m);

            Assert.Equal(0m, ozet.KargoUcreti);
            Assert.Equal(0m, ozet.Toplam);
            Assert.False(ozet.UcretsizKargoKazanildi);
            Assert.Equal(0m, ozet.UcretsizKargoyaKalan);
        }

        [Fact]
        public void Indirim_Ara_Toplami_Asamaz()
        {
            // ⚠️ Aşabilseydi toplam EKSİYE düşerdi — yani müşteriye para
            // vermiş olurduk. KuponServisi bunu zaten kontrol ediyor ama
            // "bir yerde kontrol ediliyordur" varsayımı para hesabında
            // yapılmaz.
            var hesaplayici = AyarYardimcisi.Hesaplayici(
                kargoUcreti: 0m, ucretsizKargoLimiti: 0m);

            var ozet = hesaplayici.Hesapla(araToplam: 100m, indirim: 250m);

            Assert.Equal(100m, ozet.Indirim);   // 250'ye değil 100'e kırpıldı
            Assert.Equal(0m, ozet.Toplam);
            Assert.True(ozet.Toplam >= 0m);
        }

        [Fact]
        public void Negatif_Girdiler_Sifira_Cekilir()
        {
            var hesaplayici = AyarYardimcisi.Hesaplayici(
                kargoUcreti: 0m, ucretsizKargoLimiti: 0m);

            var ozet = hesaplayici.Hesapla(araToplam: -50m, indirim: -10m);

            Assert.Equal(0m, ozet.AraToplam);
            Assert.Equal(0m, ozet.Indirim);
            Assert.Equal(0m, ozet.Toplam);
        }

        [Fact]
        public void Kargo_Ucreti_Sifirken_Ucretsiz_Kargo_Kazanilmis_Sayilmaz()
        {
            // ⚠️ "KargoUcreti == 0" ile "ücretsiz kargo KAZANILDI" farklı
            // şeyler. Mağaza kargo almıyorsa müşteriye "tebrikler, kargo
            // bedava!" demek yanlış olurdu — ödül olmayan şey ödül gibi
            // sunulmaz.
            var hesaplayici = AyarYardimcisi.Hesaplayici(
                kargoUcreti: 0m, ucretsizKargoLimiti: 500m);

            var ozet = hesaplayici.Hesapla(araToplam: 1000m, indirim: 0m);

            Assert.Equal(0m, ozet.KargoUcreti);
            Assert.False(ozet.UcretsizKargoKazanildi);
        }

        // ---------- YUVARLAMA ----------

        [Fact]
        public void Tutarlar_Kurusa_Yuvarlanir_Musteri_Lehine()
        {
            // ⚠️ AwayFromZero: 0,005 → 0,01.
            // .NET'in varsayılanı "bankacı yuvarlaması" (0,005 → 0,00)
            // ve müşteri lehine değil. Yüzdelik kupon hesabı
            // 149,999999... gibi değerler üretebiliyor; yuvarlamasaydık
            // gösterilen sayı ile veritabanına yazılan sayı ayrışırdı.
            var hesaplayici = AyarYardimcisi.Hesaplayici(
                kargoUcreti: 0m, ucretsizKargoLimiti: 0m);

            var ozet = hesaplayici.Hesapla(araToplam: 10.005m, indirim: 0m);

            Assert.Equal(10.01m, ozet.AraToplam);
            Assert.Equal(10.01m, ozet.Toplam);
        }

        [Fact]
        public void Ucretsiz_Kargoya_Kalan_Dogru_Hesaplanir()
        {
            // Mobildeki "42,50 TL daha ekle, kargo bedava" yazısının
            // kaynağı. Yanlış olsaydı müşteri ekleme yapar ama kargo
            // yine çıkardı.
            var hesaplayici = AyarYardimcisi.Hesaplayici(
                kargoUcreti: 49.90m, ucretsizKargoLimiti: 500m);

            var ozet = hesaplayici.Hesapla(araToplam: 457.50m, indirim: 0m);

            Assert.Equal(42.50m, ozet.UcretsizKargoyaKalan);
        }

        [Fact]
        public void Ucretsiz_Kargo_Limiti_Sifirsa_Esik_Kurali_Calismaz()
        {
            // Limit 0 = "ücretsiz kargo diye bir şey yok".
            // Kargo her zaman alınır, "kalan" da gösterilmez.
            var hesaplayici = AyarYardimcisi.Hesaplayici(
                kargoUcreti: 30m, ucretsizKargoLimiti: 0m);

            var ozet = hesaplayici.Hesapla(araToplam: 10_000m, indirim: 0m);

            Assert.Equal(30m, ozet.KargoUcreti);
            Assert.False(ozet.UcretsizKargoKazanildi);
            Assert.Equal(0m, ozet.UcretsizKargoyaKalan);
        }
    }
}
