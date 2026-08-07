namespace ETicaretAPI.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }

        // Hangi kategoriye ait — Category tablosuna bağlanır
        public int CategoryId { get; set; }

        // Ürünün barkodu. Panelde/müşteride id yerine bunu göstereceğiz.
        // Nullable (string?): eski ürünlerde boş kalabilsin diye.
        // Yeni üründe zorunluluğu DTO tarafında kontrol edeceğiz.
        public string? Barcode { get; set; }

        // Bize maliyeti — kâr hesabı için. Müşteriye ASLA gönderilmez.
        // Nullable (decimal?): maliyeti girilmemiş eski ürünler için.
        public decimal? Cost { get; set; }

        // ⭐ YENİ — ürün satışta mı?
        //
        // Neden silmek yerine bu bayrak:
        // Ürün silinirse ona bağlı eski sipariş kalemleri, yorumlar ve
        // favoriler sahipsiz kalır — geçmiş bozulur. Bayrak sayesinde
        // kayıt yerinde durur, sadece vitrinden çekilir.
        //
        // Neden nullable DEĞİL (bool? değil):
        // "Bilinmiyor" diye üçüncü bir durum yok. Ürün ya satıştadır ya
        // değildir. Nullable yapmak her okuma noktasında gereksiz bir
        // null kontrolü doğururdu.
        //
        // = true varsayılanı: yeni eklenen ürün doğrudan satışa açık olsun.
        // (Eski satırların doldurulması migration'da ayrıca halledilecek —
        //  C# tarafındaki bu atama veritabanına yansımaz.)
        public bool IsActive { get; set; } = true;

        // ⭐ YENİ — ÜRÜN AÇIKLAMASI
        //
        // Beden, malzeme, garanti, kutu içeriği, kullanım bilgisi...
        // Mesafeli satış mevzuatında "malın temel nitelikleri" olarak
        // zorunlu tutulan bilgi.
        //
        // Neden nullable?
        // Mevcut ürünlerde boş kalacak, admin zamanla dolduracak.
        // Zorunlu yapsaydık migration'ın var olan tüm ürünlere bir
        // şey yazması gerekirdi — ya uydurma metin ya boş string.
        // "Boş bırakmak, uydurmaktan iyidir."
        //
        // ⚠️ NEDEN OrderItem'A DONDURULMUYOR?
        // Fiyatı ve ürün adını dondurduk çünkü onlar SÖZLEŞMENİN
        // parçası: "şu üründen şu fiyata aldım." Açıklama ise bir
        // pazarlama metnidir; sonradan değişmesi geçmiş siparişi
        // geçersiz kılmaz. Mesafeli satış sözleşmesinin kendisi
        // Aşama 10'da ayrıca saklanacak.
        public string? Description { get; set; }

        // ⭐ YENİ — KDV ORANI (yüzde olarak: 1, 10, 20)
        //
        // ⚠️ FİYAT KDV DAHİLDİR. Bu oran Price'ın ÜSTÜNE eklenmez,
        // İÇİNDEN ayrıştırılır:
        //
        //     KDV = Fiyat × Oran / (100 + Oran)
        //
        // Türkiye'de tüketiciye gösterilen fiyatın tüm vergiler dahil
        // nihai fiyat olması zorunlu. Fiyatın üstüne eklemek hem yasaya
        // aykırı olurdu hem de sepette gösterilen tutarla ödenen tutarı
        // ayrıştırırdı.
        //
        // NEDEN ÜRÜNE ÖZEL, NEDEN TEK GLOBAL ORAN DEĞİL?
        // Türkiye'de yürürlükte üç oran var: %1 (temel gıda), %10
        // (bazı gıda ve hizmetler), %20 (genel). Tek bir global oran
        // kullansaydık gıda satan bir mağaza sistemi hiç kullanamazdı.
        //
        // NEDEN int, NEDEN decimal DEĞİL?
        // Oranlar tam sayı. decimal yapmak "%20,5" gibi var olmayan
        // bir esneklik vaat eder ve karşılaştırmalarda kuruş hatası
        // riski açardı. Ayrıştırma hesabında zaten decimal'e yükseliyor.
        //
        // NEDEN nullable DEĞİL?
        // Her ürünün bir KDV oranı vardır. "Bilinmiyor" diye bir durum
        // yok — IsActive'deki gerekçenin aynısı.
        //
        // = 20 varsayılanı: yeni ürün genel orana düşer. Mevcut ürünler
        // migration'da 20 ile dolduruluyor ve bu UYDURMA DEĞİL: bugüne
        // kadar sistemde KDV hiç yoktu, hepsi fiilen genel orana tabiydi.
        //
        // ⚠️ OrderItem'a DONDURULUYOR — Description'ın aksine. Fark şu:
        // açıklama pazarlama metnidir, KDV oranı ise faturanın parçasıdır.
        // Oran yasayla değişirse eski faturaların bozulmaması gerekir.
        public int VatRate { get; set; } = 20;


        // ⭐ YENİ (4.8) — ARŞİVLENDİ Mİ?
        //
        // ⚠️ BU, IsActive'İN İKİZİ DEĞİL — ÜÇÜNCÜ BİR SEVİYE.
        //
        // Üç seviye var ve üçü FARKLI sorulara cevap veriyor:
        //
        //   IsActive = false  → "vitrinden çek"
        //       Müşteri göremez, satın alamaz. Admin listesinde
        //       DURUR çünkü yarın tekrar satışa açılabilir.
        //       Geçici bir karar.
        //
        //   ArsivlendiMi = true → "gözümün önünden çek"
        //       Admin listesinde de görünmez. Ürün bir daha
        //       satılmayacak ama satırı duruyor: geçmiş siparişler,
        //       yorumlar ve stok defteri ona bağlı.
        //       Kalıcı ama geri alınabilir bir karar.
        //
        //   Fiziksel silme → satır yok olur
        //       Yalnızca "yanlışlıkla oluşturuldu, hiç kullanılmadı"
        //       durumu için. İşlem geçmişi olan ürün silinemez.
        //
        // İkisini tek alana sıkıştırmak (örn. bir "durum" enum'u)
        // mümkündü ama yanlış olurdu: bir ürün AYNI ANDA hem pasif
        // hem arşivli olabilir ve tek alan bu ikisini birden
        // anlatamaz.
        //
        // ⚠️ nullable DEĞİL, varsayılan false. "Bilinmiyor" diye bir
        // durum yok — IsActive ve VatRate'teki gerekçenin aynısı.
        // Mevcut ürünlerin hepsi arşivsiz doğuyor ve bu uydurma
        // değil: bugüne kadar arşiv diye bir kavram yoktu.
        public bool ArsivlendiMi { get; set; } = false;
    }
}