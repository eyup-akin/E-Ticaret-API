using ETicaretAPI.Services;
using Microsoft.Extensions.Configuration;

namespace ETicaretAPI.Tests
{
    // Testlerin MagazaAyarlari üretmesi için ortak yardımcı.
    //
    // ⚠️ NEDEN MOCK KÜTÜPHANESİ YOK?
    //
    // MagazaAyarlari yalnızca IConfiguration'a bakıyor ve .NET'in kendi
    // bellek içi sağlayıcısı (AddInMemoryCollection) bunun için hazır.
    // Moq gibi bir bağımlılık eklemek, gerçek sınıfın gerçek okuma
    // yolunu test etmek yerine sahte bir davranışı test etmek olurdu.
    //
    // Test edilen üç servisin (SepetHesaplayici, KdvHesaplayici,
    // IadeHesaplayici) hiçbiri DbContext almıyor — bu yüzden veritabanı
    // da, mock da gerekmiyor. Saf girdi/çıktı.
    internal static class AyarYardimcisi
    {
        internal static MagazaAyarlari Ayarlar(
            decimal kargoUcreti = 49.90m,
            decimal ucretsizKargoLimiti = 500m)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Magaza:KargoUcreti"] = kargoUcreti.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),

                    ["Magaza:UcretsizKargoLimiti"] = ucretsizKargoLimiti.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                })
                .Build();

            return new MagazaAyarlari(config);
        }

        internal static SepetHesaplayici Hesaplayici(
            decimal kargoUcreti = 49.90m,
            decimal ucretsizKargoLimiti = 500m)
        {
            return new SepetHesaplayici(Ayarlar(kargoUcreti, ucretsizKargoLimiti));
        }
    }
}
