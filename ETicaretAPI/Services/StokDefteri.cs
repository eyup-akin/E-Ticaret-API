using ETicaretAPI.Data;
using ETicaretAPI.Models;

namespace ETicaretAPI.Services
{
    // ============================================================
    //  STOK DEFTERİ YARDIMCISI
    //
    //  NEDEN AYRI BİR SERVİS?
    //  Beş ayrı yerden hareket yazılacak: sipariş, iki iptal ucu,
    //  ürün düzenleme, Excel içe aktarma. Her birine aynı beş
    //  satırı kopyalasaydık:
    //    • Biri unutulur → defterde sessiz boşluk
    //    • Alan adı değişince beş yere dokunmak gerekir
    //    • Yeni bir alan eklenince (örn. depo id) yine beş yer
    //
    //  Tek nokta olunca unutmak imkânsız hale geliyor.
    //  GuvenliGonderAsync'te aynı mantığı e-postalar için
    //  kurmuştuk.
    //
    //  ⚠️ SaveChanges ÇAĞIRMIYOR — bilerek.
    //  Bu kayıt, tetikleyen işlemle AYNI transaction'da yazılmalı.
    //  Kendi başına kaydetseydi, sipariş rollback olduğunda
    //  defterde "satıldı" yazan hayalet bir kayıt kalırdı.
    //  Kaydetme sorumluluğu çağırana ait.
    // ============================================================
    public class StokDefteri
    {
        private readonly AppDbContext _context;

        public StokDefteri(AppDbContext context)
        {
            _context = context;
        }


        // ------------------------------------------------------------
        //  Bir stok hareketi NESNESİ üretir — context'e EKLEMEZ.
        //
        //  NEDEN İKİ AYRI METOT VAR (Olustur ve Ekle)?
        //
        //  Sipariş oluştururken bir sıralama problemi var: hareketi
        //  yazdığımız anda siparişin Id'si henüz yok (sipariş
        //  kaydedilmemiş). Referansı sonradan doldurmak istiyoruz.
        //
        //  Nesneyi context'e ERKEN eklersek şu olur:
        //    1. Hareketler context'e eklenir (Added)
        //    2. Sipariş eklenir, SaveChanges çağrılır
        //    3. ⚠️ SaveChanges SADECE siparişi değil, context'teki
        //       HER Added nesneyi yazar — hareketler de diske düşer,
        //       ReferansId NULL olarak
        //    4. Referansı doldurmak için geri döndüğümüzde iş işten
        //       geçmiş olur
        //
        //  Bu yüzden Olustur sadece nesneyi verir; context'e eklemeyi
        //  çağıran, doğru anı seçerek kendisi yapar.
        //
        //  Ekle ise tek adımlı durumlar için (iptal, ürün düzenleme):
        //  oralarda referans Id'si zaten baştan biliniyor.
        // ------------------------------------------------------------
        public StockMovement Olustur(
            int urunId,
            int miktar,
            int oncekiStok,
            string sebep,
            int? kullaniciId = null,
            string? referansTipi = null,
            int? referansId = null,
            string? aciklama = null)
        {
            return new StockMovement
            {
                ProductId = urunId,
                Miktar = miktar,
                OncekiStok = oncekiStok,
                SonrakiStok = oncekiStok + miktar,
                Sebep = sebep,
                KullaniciId = kullaniciId,
                ReferansTipi = referansTipi,
                ReferansId = referansId,
                Aciklama = aciklama,
                CreatedAt = DateTime.UtcNow
            };
        }


        // ------------------------------------------------------------
        //  Bir stok hareketi kaydeder (context'e EKLER, kaydetmez).
        //
        //  Referans Id'si baştan bilinen durumlar için: sipariş
        //  iptali (sipariş zaten var), ürün düzenleme (referans yok),
        //  Excel içe aktarma (ImportJob zaten var).
        //
        //  ⚠️ SaveChanges ÇAĞIRMIYOR — bilerek.
        //  Bu kayıt, tetikleyen işlemle AYNI transaction'da yazılmalı.
        //  Kendi başına kaydetseydi, sipariş rollback olduğunda
        //  defterde "satıldı" yazan hayalet bir kayıt kalırdı.
        //  Kaydetme sorumluluğu çağırana ait.
        // ------------------------------------------------------------
        public void Ekle(
            int urunId,
            int miktar,
            int oncekiStok,
            string sebep,
            int? kullaniciId = null,
            string? referansTipi = null,
            int? referansId = null,
            string? aciklama = null)
        {
            // Miktar 0 ise hareket YOK demektir — kayıt yazmıyoruz.
            //
            // Neden önemli? Ürün düzenlemede admin stoğa hiç
            // dokunmamış olabilir. Her kaydetmede "0 değişti"
            // satırı yazsaydık defter gürültüyle dolar ve gerçek
            // hareketler kaybolurdu.
            if (miktar == 0)
            {
                return;
            }

            _context.StockMovements.Add(Olustur(
                urunId, miktar, oncekiStok, sebep,
                kullaniciId, referansTipi, referansId, aciklama));
        }
    }
}