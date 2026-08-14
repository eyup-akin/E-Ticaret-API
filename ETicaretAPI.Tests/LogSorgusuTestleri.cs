using ETicaretAPI.Support;

namespace ETicaretAPI.Tests
{
    // LogSorgusu — sistem kayıtları ekranının ortak sorgu kuralları.
    //
    // Dört sekme de bunu kullanıyor; kural burada bozulursa aynı
    // ekranın sekmeleri farklı davranır.
    public class LogSorgusuTestleri
    {
        // ---------- SAYFALAMA ----------

        [Fact]
        public void Gecerli_Degerler_Aynen_Gecer()
        {
            var (sayfa, boyut) = LogSorgusu.SayfaDuzelt(3, 50);

            Assert.Equal(3, sayfa);
            Assert.Equal(50, boyut);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(101)]
        [InlineData(100000)]
        public void Gecersiz_Sayfa_Boyutu_Varsayilana_Doner(int boyut)
        {
            // ⚠️ ?pageSize=100000 sayfalamanın koruduğu her şeyi
            // geçersiz kılardı — sunucuda zorlanıyor.
            var sonuc = LogSorgusu.SayfaDuzelt(1, boyut);

            Assert.Equal(20, sonuc.Boyut);
        }

        [Fact]
        public void Sayfa_Boyutu_Ust_Siniri_Dahildir()
        {
            // 100 geçerli, 101 değil. Sınırda bir eksik/fazla olmasın.
            Assert.Equal(100, LogSorgusu.SayfaDuzelt(1, 100).Boyut);
            Assert.Equal(20, LogSorgusu.SayfaDuzelt(1, 101).Boyut);
        }

        [Fact]
        public void Sayfa_Numarasi_Tasma_Korumasindan_Gecer()
        {
            // ⚠️ Kural SayfaSiniri'nde tek yerde; LogSorgusu kendi
            // kopyasını tutmuyor. Bu test bağın kopmadığını doğruluyor.
            var sonuc = LogSorgusu.SayfaDuzelt(int.MaxValue, 100);

            Assert.Equal(SayfaSiniri.EnBuyukSayfa, sonuc.Sayfa);
            Assert.True((sonuc.Sayfa - 1) * sonuc.Boyut >= 0);
        }

        // ---------- ÜST SINIRLI SAYIM ----------
        //
        // ⚠️ Test edilen SayimiYorumla, SayAsync değil: SayAsync
        // EF'in CountAsync'ini çağırıyor ve o sahte bir IQueryable ile
        // çalışmıyor (IAsyncQueryProvider istiyor). Kural ayrı bir saf
        // metotta durduğu için veritabanı olmadan doğrulanabiliyor.

        [Fact]
        public void Sinirin_Altinda_Gercek_Sayi_Doner()
        {
            var (toplam, asildi) = LogSorgusu.SayimiYorumla(42);

            Assert.Equal(42, toplam);
            Assert.False(asildi);
        }

        [Fact]
        public void Tam_Sinirda_Asildi_Isaretlenmez()
        {
            // ⚠️ Sınırın KENDİSİ aşım değil. 1000 kayıtta "1000+"
            // yazmak, olmayan bir belirsizlik uydurmak olurdu.
            var (toplam, asildi) = LogSorgusu.SayimiYorumla(LogSorgusu.SayimUstSiniri);

            Assert.Equal(LogSorgusu.SayimUstSiniri, toplam);
            Assert.False(asildi);
        }

        [Fact]
        public void Sinir_Asilinca_Sinir_Degeri_Ve_Bayrak_Doner()
        {
            // ⚠️ 1001 DÖNMEZ — ekran "1000+" yazıyor. Kesin sayıyı
            // vermek her sayfa yüklemesinde tam tarama demekti.
            var (toplam, asildi) = LogSorgusu.SayimiYorumla(LogSorgusu.SayimUstSiniri + 1);

            Assert.Equal(LogSorgusu.SayimUstSiniri, toplam);
            Assert.True(asildi);
        }

        [Fact]
        public void Bos_Sonuc_Sifir_Doner()
        {
            var (toplam, asildi) = LogSorgusu.SayimiYorumla(0);

            Assert.Equal(0, toplam);
            Assert.False(asildi);
        }
    }
}
