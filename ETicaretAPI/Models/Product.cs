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
    }   
}