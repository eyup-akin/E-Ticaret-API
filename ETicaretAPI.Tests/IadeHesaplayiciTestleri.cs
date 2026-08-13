using ETicaretAPI.Models;
using ETicaretAPI.Services;

namespace ETicaretAPI.Tests
{
    // IadeHesaplayici — müşteriye geri ödenecek tutar.
    //
    // Üç tüketicisi var (müşteri ekranı, admin ekranı, para iadesi anı).
    // Ayrı ayrı hesaplasalardı söylenen tutar ile ödenen tutar
    // ayrışırdı — servisin var olma sebebi bu.
    public class IadeHesaplayiciTestleri
    {
        private readonly IadeHesaplayici _hesap = new IadeHesaplayici();

        // Kısmi iade senaryolarının ortak siparişi:
        //   A (400) + B (600) = SubTotal 1000
        private static Order Siparis(
            decimal kuponIndirimi = 0m,
            decimal kombinIndirimi = 0m,
            decimal kargo = 0m)
        {
            return new Order
            {
                SubTotal = 1000m,
                DiscountAmount = kuponIndirimi,
                KombinIndirimi = kombinIndirimi,
                ShippingCost = kargo,
                Total = 1000m - kuponIndirimi - kombinIndirimi + kargo
            };
        }

        private static OrderItem Kalem(decimal birimFiyat, int adet = 1)
        {
            return new OrderItem { UnitPrice = birimFiyat, Quantity = adet };
        }

        // ---------- TÜM SİPARİŞ ----------

        [Fact]
        public void Tum_Siparis_Iadesinde_Odenen_Her_Sey_Geri_Verilir()
        {
            // Cayma hakkında standart teslimat masrafı da iade edilir,
            // o yüzden kargo dahil Total dönüyor.
            var siparis = Siparis(kuponIndirimi: 100m, kargo: 49.90m);

            var tutar = _hesap.Hesapla(siparis, kalem: null);

            Assert.Equal(siparis.Total, tutar);
            Assert.Equal(949.90m, tutar);
        }

        // ---------- KISMİ İADE ----------

        [Fact]
        public void Indirimsiz_Siparislerde_Kalem_Tutari_Aynen_Iade_Edilir()
        {
            var siparis = Siparis();

            var tutar = _hesap.Hesapla(siparis, Kalem(400m));

            Assert.Equal(400m, tutar);
        }

        [Fact]
        public void Kupon_Indirimi_Kalem_Payina_Gore_Dusulur()
        {
            // Sepetin %10'u indirilmiş; 400 TL'lik kalemin payı 40 TL.
            // Ham fiyatı geri ödersek müşteri indirim payını cebe atar.
            var siparis = Siparis(kuponIndirimi: 100m);

            var tutar = _hesap.Hesapla(siparis, Kalem(400m));

            Assert.Equal(360m, tutar);   // 400 − (100 × 400/1000)
        }

        [Fact]
        public void Kombin_Indirimi_De_Kalem_Payina_Gore_Dusulur()
        {
            // ⚠️ BU TESTİN VAR OLMA SEBEBİ BİR HATA.
            //
            // IadeHesaplayici yalnızca DiscountAmount'ı (kupon) okuyordu;
            // Order.KombinIndirimi ayrı bir alanda tutulduğu için hesaba
            // HİÇ girmiyordu. Kombin indirimli bir siparişten tek kalem
            // iade edilince müşteriye fazla para ödeniyordu.
            //
            // A (400) + B (600), %10 kombin indirimi = 100 TL.
            // A iade edilince eski kod 400 ödüyordu; doğrusu 360.
            var siparis = Siparis(kombinIndirimi: 100m);

            var tutar = _hesap.Hesapla(siparis, Kalem(400m));

            Assert.Equal(360m, tutar);
        }

        [Fact]
        public void Kupon_Ve_Kombin_Indirimi_Birlikte_Dusulur()
        {
            // İki indirim aynı siparişte olabilir: kupon 100, kombin 50.
            // Toplam 150'nin %40'ı (400/1000) = 60 TL düşülmeli.
            var siparis = Siparis(kuponIndirimi: 100m, kombinIndirimi: 50m);

            var tutar = _hesap.Hesapla(siparis, Kalem(400m));

            Assert.Equal(340m, tutar);   // 400 − 60
        }

        [Fact]
        public void Kargo_Kismi_Iadede_Geri_Verilmez()
        {
            // Sipariş yine gönderildi ve diğer ürünler müşteride kaldı;
            // teslimat masrafı yapılmış durumda.
            var siparis = Siparis(kargo: 49.90m);

            var tutar = _hesap.Hesapla(siparis, Kalem(400m));

            Assert.Equal(400m, tutar);   // kargo eklenmiyor
        }

        [Fact]
        public void Adet_Birden_Fazlaysa_Tamami_Iade_Edilir()
        {
            // ⚠️ Kalemin TAMAMI iade ediliyor, adet seçilemiyor.
            var siparis = Siparis();

            var tutar = _hesap.Hesapla(siparis, Kalem(200m, adet: 3));

            Assert.Equal(600m, tutar);
        }

        [Fact]
        public void Dondurulmus_Fiyat_Kullanilir_Guncel_Fiyat_Degil()
        {
            // OrderItem.UnitPrice sipariş anındaki fiyat. Ürünün bugünkü
            // fiyatı ne olursa olsun müşteri ÖDEDİĞİNİ geri alır.
            var siparis = Siparis();

            var tutar = _hesap.Hesapla(siparis, Kalem(birimFiyat: 250m));

            Assert.Equal(250m, tutar);
        }

        // ---------- SINIR DURUMLARI ----------

        [Fact]
        public void Iade_Tutari_Negatife_Dusmez()
        {
            // İndirim kalem tutarını aşarsa 0'a kırpılır — müşteriye
            // eksi para "iade" edilemez.
            var siparis = new Order
            {
                SubTotal = 100m,
                DiscountAmount = 100m,
                KombinIndirimi = 0m
            };

            var tutar = _hesap.Hesapla(siparis, Kalem(100m));

            Assert.Equal(0m, tutar);
            Assert.True(tutar >= 0m);
        }

        [Fact]
        public void SubTotal_Sifirsa_Indirim_Payi_Hesaplanmaz()
        {
            // Sıfıra bölme koruması: eski/bozuk kayıtlarda SubTotal 0
            // olabilir. Patlamak yerine indirimsiz davranıyoruz.
            var siparis = new Order
            {
                SubTotal = 0m,
                DiscountAmount = 50m,
                KombinIndirimi = 25m
            };

            var tutar = _hesap.Hesapla(siparis, Kalem(100m));

            Assert.Equal(100m, tutar);
        }

        [Fact]
        public void Toplam_Indirim_Iki_Alanin_Toplamidir()
        {
            // Order.ToplamIndirim, indirimi okuyan her yerin tek kaynağı.
            // Yarın üçüncü bir indirim alanı eklenirse yalnızca o özellik
            // değişecek, bu servis değişmeyecek.
            var siparis = Siparis(kuponIndirimi: 100m, kombinIndirimi: 50m);

            Assert.Equal(150m, siparis.ToplamIndirim);
        }
    }
}
