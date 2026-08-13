using ETicaretAPI.Services;

namespace ETicaretAPI.Tests
{
    // KdvHesaplayici — faturanın vergi dökümü.
    //
    // ⚠️ Bu servis TOPLAM ÜRETMEZ. Fiyatlar KDV dahil olduğu için
    // ödenecek tutar değişmiyor; burada üretilen sadece o tutarın
    // dökümü. Testler de bunu doğruluyor: matrah + vergi = dahil tutar.
    public class KdvHesaplayiciTestleri
    {
        private readonly KdvHesaplayici _kdv = new KdvHesaplayici();

        [Fact]
        public void Vergi_Fiyatin_Icinden_Ayristirilir_Ustune_Eklenmez()
        {
            // 120 TL, %20 → matrah 100, vergi 20.
            // Üstüne eklenseydi 144 çıkardı ve müşteri sepette gördüğü
            // tutardan fazlasını öderdi.
            var ozet = _kdv.Ozetle(new[] { (120m, (int?)20) });

            var satir = Assert.Single(ozet.Satirlar);

            Assert.Equal(20, satir.Oran);
            Assert.Equal(100m, satir.Matrah);
            Assert.Equal(20m, satir.Vergi);
            Assert.Equal(120m, satir.DahilTutar);
            Assert.Equal(satir.DahilTutar, satir.Matrah + satir.Vergi);
        }

        [Fact]
        public void Farkli_Oranlar_Ayri_Satirlarda_Ve_Orana_Gore_Sirali()
        {
            // ⚠️ Sepette %1 gıda ile %20 elektronik birlikte olabilir.
            // "Siparişin KDV oranı" diye tek bir şey yok — fatura satır
            // satır kesilir.
            var ozet = _kdv.Ozetle(new[]
            {
                (120m, (int?)20),
                (101m, (int?)1),
                (110m, (int?)10)
            });

            Assert.Equal(3, ozet.Satirlar.Count);

            // Orana göre ARTAN sırada
            Assert.Equal(new[] { 1, 10, 20 }, ozet.Satirlar.Select(s => s.Oran));
            Assert.True(ozet.DokumVarMi);
        }

        [Fact]
        public void Ayni_Oranli_Kalemler_Once_Toplanip_Sonra_Ayristirilir()
        {
            // ⚠️ Kalem kalem ayrıştırıp sonra toplasaydık her kalemde
            // ayrı yuvarlama olurdu ve 10 kalemlik siparişte kuruşlar
            // birikirdi. Aynı orandaki tutarlar önce toplanıyor.
            var ozet = _kdv.Ozetle(new[]
            {
                (60m, (int?)20),
                (60m, (int?)20)
            });

            var satir = Assert.Single(ozet.Satirlar);

            Assert.Equal(120m, satir.DahilTutar);
            Assert.Equal(100m, satir.Matrah);
            Assert.Equal(20m, satir.Vergi);
        }

        [Fact]
        public void Orani_Bilinmeyen_Kalem_Dokume_Girmez()
        {
            // ⚠️ Oranı null olan kalemler ESKİ siparişlerden geliyor;
            // hangi oranın uygulandığını gerçekten bilmiyoruz.
            // 0 sayıp döküme katsaydık "bu kalemde KDV alınmadı" diye
            // YANLIŞ bir iddiada bulunurduk.
            var ozet = _kdv.Ozetle(new[]
            {
                (120m, (int?)20),
                (500m, (int?)null)
            });

            var satir = Assert.Single(ozet.Satirlar);

            Assert.Equal(120m, satir.DahilTutar);   // 620 DEĞİL
            Assert.Equal(100m, ozet.ToplamMatrah);
        }

        [Fact]
        public void Hicbir_Oran_Bilinmiyorsa_Dokum_Yok()
        {
            // DokumVarMi = false → ekran KDV bölümünü hiç çizmiyor.
            // "KDV: 0,00 TL" yazmak eksik değil, YANLIŞ bilgi olurdu.
            var ozet = _kdv.Ozetle(new[]
            {
                (500m, (int?)null),
                (300m, (int?)null)
            });

            Assert.Empty(ozet.Satirlar);
            Assert.False(ozet.DokumVarMi);
            Assert.Equal(0m, ozet.ToplamMatrah);
            Assert.Equal(0m, ozet.ToplamVergi);
        }

        [Fact]
        public void Bos_Liste_Cokmez()
        {
            var ozet = _kdv.Ozetle(Array.Empty<(decimal, int?)>());

            Assert.Empty(ozet.Satirlar);
            Assert.False(ozet.DokumVarMi);
        }

        [Fact]
        public void Toplamlar_Satirlarin_Toplamina_Esit()
        {
            var ozet = _kdv.Ozetle(new[]
            {
                (120m, (int?)20),
                (110m, (int?)10),
                (49.90m, (int?)20)     // kargo da döküme giriyor
            });

            Assert.Equal(ozet.Satirlar.Sum(s => s.Matrah), ozet.ToplamMatrah);
            Assert.Equal(ozet.Satirlar.Sum(s => s.Vergi), ozet.ToplamVergi);
        }

        [Fact]
        public void Sifir_Oran_Gecerli_Bir_Orandir()
        {
            // %0 ile "oran bilinmiyor" (null) farklı şeyler:
            // biri ölçülmüş bir gerçek, diğeri bilgi eksikliği.
            var ozet = _kdv.Ozetle(new[] { (100m, (int?)0) });

            var satir = Assert.Single(ozet.Satirlar);

            Assert.Equal(0, satir.Oran);
            Assert.Equal(100m, satir.Matrah);
            Assert.Equal(0m, satir.Vergi);
            Assert.True(ozet.DokumVarMi);
        }
    }
}
