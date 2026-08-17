using ETicaretAPI.Services;

namespace ETicaretAPI.Tests
{
    // IyzicoSepetiKurucu — ödeme akışının en kırılgan yeri.
    //
    // iyzico "price = kalemler toplamı" kuralını kuruşu kuruşuna
    // dayatıyor ve bir kuruş şaşarsa isteği tümden reddediyor. Bizde
    // ise kupon + kombin indirimi ve kargo var. Buradaki bir hata
    // "ödeme hiç başlamıyor" ya da "yanlış tutar çekiliyor" demek.
    public class IyzicoSepetiKurucuTestleri
    {
        private static readonly IyzicoSepetiKurucu Kurucu = new();

        private static IyzicoKalemGirdisi Kalem(int id, decimal tutar) =>
            new(id, $"Urun {id}", "Genel", tutar);


        // ---------- TOPLAM EŞİTLİĞİ ----------

        [Fact]
        public void Indirimsiz_Kargosuz_Kalemler_Aynen_Gecer()
        {
            var sonuc = Kurucu.Kur(
                new[] { Kalem(1, 100m), Kalem(2, 250m) },
                kargoUcreti: 0m,
                siparisToplami: 350m);

            Assert.Equal(350m, sonuc.Price);
            Assert.Equal(2, sonuc.Kalemler.Count);
            Assert.Equal(100m, sonuc.Kalemler[0].Tutar);
            Assert.Equal(250m, sonuc.Kalemler[1].Tutar);
        }

        [Fact]
        public void Kargo_Ayri_Sanal_Kalem_Olarak_Eklenir()
        {
            var sonuc = Kurucu.Kur(
                new[] { Kalem(1, 300m) },
                kargoUcreti: 49.90m,
                siparisToplami: 349.90m);

            Assert.Equal(349.90m, sonuc.Price);
            Assert.Equal(2, sonuc.Kalemler.Count);

            var kargo = sonuc.Kalemler[1];
            Assert.Equal(IyzicoSepetiKurucu.KargoKalemId, kargo.Id);
            Assert.False(kargo.Fiziksel);
            Assert.Equal(49.90m, kargo.Tutar);
            Assert.Null(kargo.OrderItemId);
        }

        [Fact]
        public void Ucretsiz_Kargo_Kalemi_Hic_Eklenmez()
        {
            // ⚠️ 0 TL'lik kalemi iyzico reddediyor; eklememek şart.
            var sonuc = Kurucu.Kur(
                new[] { Kalem(1, 600m) },
                kargoUcreti: 0m,
                siparisToplami: 600m);

            Assert.Single(sonuc.Kalemler);
            Assert.Equal(600m, sonuc.Price);
        }

        [Fact]
        public void Indirim_Kalemlere_Oranla_Dagitilir()
        {
            // 100 + 300 = 400, indirim 40 → hedef 360.
            // Oranlar 1/4 ve 3/4 → 90 ve 270.
            var sonuc = Kurucu.Kur(
                new[] { Kalem(1, 100m), Kalem(2, 300m) },
                kargoUcreti: 0m,
                siparisToplami: 360m);

            Assert.Equal(90m, sonuc.Kalemler[0].Tutar);
            Assert.Equal(270m, sonuc.Kalemler[1].Tutar);
            Assert.Equal(360m, sonuc.Price);
        }

        [Fact]
        public void Kuponlu_Kombinli_Kargolu_Sepette_Toplam_Tam_Tutar()
        {
            // Gerçekçi senaryo: üç kalem, iki indirim, kargo ücretli.
            // 249.90 + 89.50 + 1299.99 = 1639.39
            // indirim 150 + 75 = 225 → 1414.39 + 49.90 kargo = 1464.29
            var sonuc = Kurucu.Kur(
                new[] { Kalem(1, 249.90m), Kalem(2, 89.50m), Kalem(3, 1299.99m) },
                kargoUcreti: 49.90m,
                siparisToplami: 1464.29m);

            Assert.Equal(1464.29m, sonuc.Price);
            Assert.Equal(1464.29m, sonuc.Kalemler.Sum(k => k.Tutar));
        }


        // ---------- YUVARLAMA ----------

        [Fact]
        public void Bolunemeyen_Indirimde_Kurus_Kaybolmaz()
        {
            // 3 eşit kalemden 10 TL indirim: 33.333... çıkıyor, üçü de
            // yuvarlanınca toplam şaşar. Artık en büyük kaleme yazılıyor.
            var sonuc = Kurucu.Kur(
                new[] { Kalem(1, 100m), Kalem(2, 100m), Kalem(3, 100m) },
                kargoUcreti: 0m,
                siparisToplami: 290m);

            Assert.Equal(290m, sonuc.Price);
            Assert.Equal(290m, sonuc.Kalemler.Sum(k => k.Tutar));
        }

        [Theory]
        [InlineData(0.01)]
        [InlineData(0.03)]
        [InlineData(7.77)]
        [InlineData(99.99)]
        [InlineData(333.33)]
        public void Farkli_Indirimlerde_Toplam_Her_Zaman_Tutar(decimal indirim)
        {
            var kalemler = new[] { Kalem(1, 129.90m), Kalem(2, 45.55m), Kalem(3, 899.00m) };
            var araToplam = kalemler.Sum(k => k.SatirTutari);
            var toplam = araToplam - indirim + 49.90m;

            var sonuc = Kurucu.Kur(kalemler, kargoUcreti: 49.90m, siparisToplami: toplam);

            Assert.Equal(toplam, sonuc.Price);
            Assert.Equal(toplam, sonuc.Kalemler.Sum(k => k.Tutar));
        }


        // ---------- UÇ DURUMLAR ----------

        [Fact]
        public void Tek_Kalemli_Sepet_Calisir()
        {
            var sonuc = Kurucu.Kur(
                new[] { Kalem(7, 199.99m) },
                kargoUcreti: 0m,
                siparisToplami: 149.99m);

            Assert.Single(sonuc.Kalemler);
            Assert.Equal(149.99m, sonuc.Kalemler[0].Tutar);
            Assert.Equal("7", sonuc.Kalemler[0].Id);
        }

        [Fact]
        public void Indirim_Bir_Kalemi_Sifira_Indirse_Alt_Sinira_Cekilir()
        {
            // 5 + 995 = 1000, indirim 900 → hedef 100.
            // Küçük kalemin payı 0.50 → 0.01'in üstünde, ama sınır
            // kuralının çalıştığını daha uçta görelim: indirim 999.
            var sonuc = Kurucu.Kur(
                new[] { Kalem(1, 5m), Kalem(2, 995m) },
                kargoUcreti: 0m,
                siparisToplami: 1m);

            Assert.All(sonuc.Kalemler, k => Assert.True(k.Tutar >= 0.01m));
            Assert.Equal(1m, sonuc.Price);
        }

        [Fact]
        public void Alt_Sinirin_Altina_Dusen_Sepet_Reddedilir()
        {
            // 3 kalem için en az 0.03 TL gerekiyor; 0.02 ile kurulamaz.
            // Sessizce yanlış tutar göndermektense patlaması doğru.
            Assert.Throws<InvalidOperationException>(() => Kurucu.Kur(
                new[] { Kalem(1, 100m), Kalem(2, 100m), Kalem(3, 100m) },
                kargoUcreti: 0m,
                siparisToplami: 0.02m));
        }

        [Fact]
        public void Bos_Sepet_Reddedilir()
        {
            Assert.Throws<ArgumentException>(() => Kurucu.Kur(
                Array.Empty<IyzicoKalemGirdisi>(),
                kargoUcreti: 0m,
                siparisToplami: 100m));
        }

        [Fact]
        public void Negatif_Kargo_Reddedilir()
        {
            Assert.Throws<ArgumentException>(() => Kurucu.Kur(
                new[] { Kalem(1, 100m) },
                kargoUcreti: -1m,
                siparisToplami: 99m));
        }

        [Fact]
        public void Kalem_Adi_Bos_Gelirse_Yedek_Metin_Yazilir()
        {
            // ⚠️ iyzico boş ad kabul etmiyor; ürün adı bir şekilde boş
            // kalırsa ödeme tümden başlamazdı.
            var sonuc = Kurucu.Kur(
                new[] { new IyzicoKalemGirdisi(1, "   ", "", 100m) },
                kargoUcreti: 0m,
                siparisToplami: 100m);

            Assert.False(string.IsNullOrWhiteSpace(sonuc.Kalemler[0].Ad));
            Assert.False(string.IsNullOrWhiteSpace(sonuc.Kalemler[0].Kategori));
        }

        [Fact]
        public void Uzun_Kalem_Adi_Kirpilir()
        {
            var uzunAd = new string('A', 500);

            var sonuc = Kurucu.Kur(
                new[] { new IyzicoKalemGirdisi(1, uzunAd, "Genel", 100m) },
                kargoUcreti: 0m,
                siparisToplami: 100m);

            Assert.Equal(200, sonuc.Kalemler[0].Ad.Length);
        }

        [Fact]
        public void Kalem_Kimligi_OrderItemId_Ile_Eslesiyor()
        {
            // ⚠️ Kısmi iade bu eşleşmeye dayanıyor: iyzico'nun döndürdüğü
            // itemId'den OrderItem'a geri dönebilmemiz gerekiyor.
            var sonuc = Kurucu.Kur(
                new[] { Kalem(41, 100m), Kalem(42, 100m) },
                kargoUcreti: 10m,
                siparisToplami: 210m);

            Assert.Equal(41, sonuc.Kalemler[0].OrderItemId);
            Assert.Equal("41", sonuc.Kalemler[0].Id);
            Assert.Equal(42, sonuc.Kalemler[1].OrderItemId);
        }
    }
}
