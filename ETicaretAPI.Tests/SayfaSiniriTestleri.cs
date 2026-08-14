using ETicaretAPI.Support;

namespace ETicaretAPI.Tests
{
    // SayfaSiniri — sayfa numarasının taşma koruması.
    //
    // ⚠️ Bu sınıf bir GERÇEK HATADAN doğdu: pageSize 100'e sınırlıydı
    // ama page sınırsızdı ve `Skip((page - 1) * pageSize)` çarpımı
    // taşıp negatife dönüyordu. SQL Server isteği 500 ile reddediyordu.
    public class SayfaSiniriTestleri
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(500)]
        [InlineData(SayfaSiniri.EnBuyukSayfa)]
        public void Gecerli_Sayfa_Numarasi_Aynen_Gecer(int sayfa)
        {
            // Sınırın KENDİSİ de geçerli — kırpma bir eksik başlamamalı.
            Assert.Equal(sayfa, SayfaSiniri.Duzelt(sayfa));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void Sifir_Ve_Negatif_Bire_Cekilir(int sayfa)
        {
            Assert.Equal(1, SayfaSiniri.Duzelt(sayfa));
        }

        [Theory]
        [InlineData(SayfaSiniri.EnBuyukSayfa + 1)]
        [InlineData(2000000000)]
        [InlineData(int.MaxValue)]
        public void Asiri_Buyuk_Sayfa_Ust_Sinira_Cekilir(int sayfa)
        {
            Assert.Equal(SayfaSiniri.EnBuyukSayfa, SayfaSiniri.Duzelt(sayfa));
        }

        // ⚠️⚠️ ASIL REGRESYON TESTİ — hatanın kendisi buydu.
        //
        // Düzeltmeden önce page=2000000000 & pageSize=100 çarpımı
        // int'e sığmayıp NEGATİF oluyordu.
        [Theory]
        [InlineData(int.MinValue)]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2000000000)]
        [InlineData(int.MaxValue)]
        public void Skip_Hesabi_Hicbir_Girdide_Negatife_Donmez(int sayfa)
        {
            // 100 = projedeki en büyük pageSize; en kötü durum bu.
            var offset = (SayfaSiniri.Duzelt(sayfa) - 1) * 100;

            Assert.True(offset >= 0, $"OFFSET negatif oldu: {offset}");
        }

        [Fact]
        public void En_Kotu_Durumda_Bile_Int_Tasmasi_Olmaz()
        {
            // long ile hesaplayıp int sonucuyla karşılaştırıyoruz:
            // eşit değillerse taşma olmuş demektir.
            var sayfa = SayfaSiniri.Duzelt(int.MaxValue);

            var intSonuc = (sayfa - 1) * 100;
            var longSonuc = ((long)sayfa - 1) * 100;

            Assert.Equal(longSonuc, intSonuc);
        }
    }
}
