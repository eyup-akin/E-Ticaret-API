using System.ComponentModel.DataAnnotations.Schema;

namespace ETicaretAPI.Models
{
    public class Order
    {
        // Teknik anahtar — veritabanının iç kimliği, URL'lerde kullanılır
        public int Id { get; set; }

        // ⭐ YENİ — MÜŞTERİYE GÖSTERİLEN NUMARA
        // Format: SP-260724-4821
        // Id sıralı ve tahmin edilebilir olduğu için dışarı bunu veriyoruz.
        // Benzersizliğini AppDbContext'teki unique index garanti eder.
        public string OrderNumber { get; set; } = string.Empty;

        public int UserId { get; set; }
        public int AddressId { get; set; }
        public decimal Total { get; set; }
        public string Status { get; set; } = "hazirlaniyor"; // kargo durumu

        // Ödeme bilgileri
        public string PaymentStatus { get; set; } = "beklemede"; // beklemede / odendi / iade_edildi
        public string CardLast4 { get; set; } = string.Empty;

        // ⭐ KUPON — hepsi DONDURULMUŞ.
        // Kupon sonradan silinse/değiştirilse bile bu sipariş ne indirim
        // aldığını hatırlar. (UnitPrice ve Shipping* ile aynı mantık.)

        // İndirimden ÖNCEKİ tutar. Total = SubTotal - DiscountAmount.
        // Türetilebilir ama saklıyoruz: ileride kargo ücreti gibi kalemler
        // girerse formül bozulur, para hesabında risk almıyoruz.
        public decimal SubTotal { get; set; }

        // Kullanılan kupon kodu. Boş = kupon kullanılmadı.
        public string CouponCode { get; set; } = string.Empty;

        // Uygulanan indirim tutarı. 0 = indirim yok.
        public decimal DiscountAmount { get; set; }

        // ⭐ YENİ — kombin indirimi, kupon indiriminden AYRI dondurulur.
        // DiscountAmount'a eklemek o alanın anlamını ("kupon indirimi")
        // sessizce değiştirirdi.
        public decimal KombinIndirimi { get; set; } = 0;

        // ⭐ YENİ — SİPARİŞE UYGULANAN TOPLAM İNDİRİM
        //
        // ⚠️ NEDEN VAR? İndirim artık İKİ alana bölünmüş durumda ve
        // ikisini toplamayı unutmak sessiz bir para hatası doğuruyor:
        // IadeHesaplayici yalnızca DiscountAmount'ı okuduğu için kısmi
        // iadelerde kombin payını düşmüyordu, yani müşteriye fazla para
        // ödeniyordu.
        //
        // Toplamı burada tanımlayınca kural TEK yerde yaşıyor. Yarın
        // üçüncü bir indirim alanı eklenirse (kampanya, hediye çeki)
        // yalnızca bu satır değişir; indirimi okuyan hiçbir yer
        // güncellenmek zorunda kalmaz.
        //
        // ⚠️ SAKLANMIYOR, hesaplanıyor. Saklasaydık iki alanla üçüncü
        // bir sayı olurdu ve bir gün ayrışırdı — "türetilmiş değer ayrı
        // state'te tutulmaz".
        [NotMapped]
        public decimal ToplamIndirim => DiscountAmount + KombinIndirimi;

        // ⭐ YENİ — DONDURULMUŞ TESLİMAT ADRESİ
        // AddressId hâlâ duruyor ama artık ona GÜVENMİYORUZ.
        // Müşteri adresini sonradan düzenlerse/silerse eski siparişin
        // kargo etiketi yanlış çıkardı. Sipariş anındaki hali buraya
        // kopyalanır ve bir daha değişmez.
        // (UnitPrice ve CardLast4'te uyguladığımız mantığın aynısı.)
        public string ShippingFullName { get; set; } = string.Empty;
        public string ShippingTitle { get; set; } = string.Empty;
        public string ShippingCity { get; set; } = string.Empty;
        public string ShippingFullAddress { get; set; } = string.Empty;

        // ⭐ YENİ — dondurulmuş alıcı telefonu.
        // Müşteri numarasını değiştirse bile bu sipariş hangi numarayla
        // gönderildiyse onu hatırlar.
        public string ShippingPhone { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // İptal bilgileri — nullable, iptal edilmemiş siparişlerde boş
        public string? CancelReason { get; set; }
        public DateTime? CancelledAt { get; set; }

        // ⭐ YENİ — KARGO TAKİP BİLGİLERİ
        //
        // Hepsi nullable (?) çünkü sipariş oluşturulduğu anda bunların
        // hiçbiri bilinmiyor. Sipariş hayat döngüsünde ilerledikçe
        // doluyorlar:
        //
        //   hazirlaniyor    → hepsi null
        //   kargoda         → ShippingCompany, TrackingNumber, ShippedAt dolu
        //   teslim_edildi   → DeliveredAt de dolu
        //
        // Bu, CancelReason/CancelledAt ikilisiyle birebir aynı desen:
        // "henüz olmamış bir olayın bilgisi null'dır."
        //
        // ⚠️ Neden bunlar DONDURULMUŞ alan sayılmıyor?
        // ShippingFullName gibi alanlar başka bir tablodan (Addresses)
        // kopyalanıyor — kaynak değişse bile sipariş eski hali hatırlasın
        // diye. Bunlar ise başka hiçbir yerde yaşamıyor, doğrudan buraya
        // yazılıyor. Kopya değil, asıl veri.

        // Kargo firmasının adı. Örn: "Yurtiçi Kargo"
        //
        // Neden ayrı bir ShippingCompanies TABLOSU değil de düz metin?
        // Firma listesi 4-5 elemanlı, nadiren değişiyor ve firmanın
        // adından başka hiçbir özelliğini saklamıyoruz. Tablo açmak
        // her sipariş sorgusuna bir JOIN eklerdi — bedeli faydasından
        // fazla. Liste appsettings'ten okunuyor.
        //
        // Ayrıca burada saklanan metin bir DONDURULMUŞ değer gibi
        // davranıyor: firma listeden çıksa bile eski sipariş hangi
        // firmayla gittiğini hatırlıyor.
        public string? ShippingCompany { get; set; }

        // Kargo firmasının verdiği takip numarası.
        //
        // Neden string, neden long/int değil?
        // Numaralar firma bazında farklı biçimde: bazıları harf içeriyor
        // ("YK1234567890"), bazıları başında sıfırla başlıyor ("0012345")
        // ve sayıya çevirirsek o sıfırlar kaybolur. Üzerinde toplama
        // çıkarma yapmadığımız her "numara" aslında bir METİNDİR.
        public string? TrackingNumber { get; set; }

        // Kargoya verildiği an (UTC).
        // Durum "kargoda" yapıldığında sunucu otomatik yazar — admin
        // elle girmez, çünkü "ne zaman kargoya verdim" sorusunun cevabı
        // tıklanan andır.
        public DateTime? ShippedAt { get; set; }

        // Teslim edildiği an (UTC).
        public DateTime? DeliveredAt { get; set; }

        // ⭐ YENİ — MÜŞTERİ NOTU
        //
        // "Kapıya bırakın", "zili çalmayın bebek uyuyor", "iş yerine
        // öğleden sonra getirin" gibi.
        //
        // Sipariş anında müşteri yazar, bir daha DEĞİŞMEZ. Adres gibi
        // dondurulmuş sayılır: kargo hazırlanırken okunacak talimat,
        // sonradan değiştirilebilir olsa kargo çıktıktan sonra
        // değiştirilip "ben böyle yazmıştım" tartışması çıkardı.
        public string? CustomerNote { get; set; }

        // ⭐ YENİ — KARGO ÜCRETİ (dondurulmuş)
        //
        // Sipariş anında SepetHesaplayici'nin verdiği tutar buraya
        // kopyalanır. Ayardaki ücret sonradan değişse bile bu sipariş
        // ne ödendiğini hatırlar.
        //
        // ⚠️ nullable DEĞİL: her siparişin bir kargo ücreti vardır
        // (ücretsizse 0). "Bilinmiyor" diye bir durumu yok. Migration
        // eski siparişlere 0 yazacak ve bu doğru: o siparişlerde kargo
        // gerçekten alınmamıştı.
        public decimal ShippingCost { get; set; } = 0;

        // ⭐ YENİ — DONDURULMUŞ KARGO KDV ORANI
        //
        // Kargo bir HİZMETTİR ve KDV'ye tabidir. ShippingCost da tıpkı
        // ürün fiyatları gibi KDV DAHİL bir tutar; oran onun üstüne
        // eklenmez, içinden ayrıştırılır.
        //
        // NEDEN AYRI BİR ALAN, NEDEN ÜRÜN ORANINI KULLANMIYORUZ?
        // Sepette %1'lik gıda ile %20'lik elektronik birlikte olabilir —
        // "siparişin KDV oranı" diye tek bir şey yok. Kargo kendi
        // hizmetidir ve kendi oranına tabidir. Kalemlerden birinin
        // oranını ödünç almak, sepetin içeriği değişince kargo KDV'sinin
        // de değişmesi gibi saçma bir sonuç doğururdu.
        //
        // Değeri MagazaAyarlari.KargoKdvOrani'ndan sipariş anında
        // kopyalanır. ShippingCost'un hemen yanında duruyor çünkü ikisi
        // birlikte anlam taşıyor: tutar ve o tutarın vergi oranı.
        //
        // ⚠️ nullable ve migration'da DOLDURULMUYOR — OrderItem.VatRate
        // ile aynı gerekçe. Eski siparişlerde hangi oranın uygulandığını
        // bilmiyoruz; zaten kargo da alınmamıştı. 0 yazmak "KDV'siz kargo
        // uygulandı" diye yanlış bir iddia olurdu.
        public int? ShippingVatRate { get; set; }

        // ⭐ YENİ — ÇİFT SİPARİŞ KORUMASI (idempotency)
        //
        // Mobil, sipariş ekranı açılınca bir kere rastgele anahtar
        // üretip her istekte aynısını gönderiyor. Aynı anahtarla
        // ikinci bir sipariş oluşamaz.
        //
        // Neden nullable?
        //   1) Bu alandan ÖNCE oluşmuş tüm siparişlerde değer yok —
        //      nullable olmasa migration backfill isterdi ve geçmişe
        //      uydurma anahtarlar yazmak zorunda kalırdık
        //   2) Admin panelinden veya ileride başka bir kanaldan gelen
        //      istekler anahtar göndermeyebilir; anahtarsız istek
        //      geçerli bir istektir, sadece korumasızdır
        //
        // Benzersizliğini AppDbContext'teki (UserId, IdempotencyKey)
        // bileşik unique index garanti eder.
        public string? IdempotencyKey { get; set; }
    }
}
