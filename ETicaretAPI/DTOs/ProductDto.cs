namespace ETicaretAPI.DTOs
{
    public class ProductDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public int CategoryId { get; set; }

        // ⭐ YENİ — barkod. Herkese açık (gizli bilgi değil).
        public string? Barcode { get; set; }

        // ⭐ YENİ — maliyet. SADECE admin isteklerinde dolar,
        // müşteri/misafir isteğinde null gider (controller'da hallediyoruz).
        public decimal? Cost { get; set; }

        // Listelerde tek resim yeter. Ana resim yoksa ilk resim, o da yoksa null.
        public string? MainImageUrl { get; set; }

        // Detay ekranında galeri için tüm resimler
        public List<ProductImageDto> Images { get; set; } = new List<ProductImageDto>();

        // Puan özeti (yorum yoksa ikisi de 0 kalır)
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }


        // ⭐ YENİ — ürün satışta mı?
        //
        // Neden Cost gibi müşteriden gizlemiyoruz:
        // Maliyet ticari sır (rakip marjını öğrenir). Aktiflik ise sır değil —
        // zaten müşteri listesinde pasif ürünler HİÇ görünmeyecek, dolayısıyla
        // müşteriye giden her kayıtta bu alan zaten true olacak. Gizlemenin
        // koruduğu bir şey yok, gereksiz özel durum yaratmış oluruz.
        //
        // Asıl tüketicisi admin paneli: listede rozet, formda anahtar.
        public bool IsActive { get; set; }

        // ⭐ YENİ — ürün açıklaması.
        //
        // ⚠️ SADECE DETAY UCUNDA DOLAR, liste ucunda null kalır.
        //
        // Neden? "Liste ucu ÖZET, detay ucu TAM veri döndürür."
        // 2000 karakterlik bir metin, 50 ürünlük bir listede 100 KB
        // gereksiz veri demek. Mobil kullanıcı ana sayfada bu metnin
        // hiçbirini görmüyor — ama mobil veriyle indiriyor.
        //
        // Cost alanındaki desenin akrabası: orada güvenlik gerekçesiyle
        // kısıtlıyorduk, burada performans gerekçesiyle.
        public string? Description { get; set; }


        public int FavoriteCount { get; set; }

        // ⭐ YENİ — KDV oranı (%).
        //
        // ⚠️ NEDEN MÜŞTERİYE DE GÖNDERİLİYOR?
        //
        // Cost'u gizliyoruz çünkü ticari sır. KDV oranı ise tam tersi:
        // faturada zaten yazması gereken, yasal olarak açık bir bilgi.
        // Gizlemenin koruduğu bir şey yok.
        //
        // ⚠️ Fiyat KDV DAHİL olduğu için mobil bu alanı fiyat
        // hesabında KULLANMAZ. Yalnızca gösterim için — ve şu an
        // hiçbir müşteri ekranı göstermiyor. Asıl tüketicisi admin
        // paneli: ürün formundaki oran seçici.
        //
        // Liste ucunda da dolu geliyor (Description'ın aksine): tek bir
        // int, veri yükü yok denecek kadar az. Kural "her şeyi kes"
        // değil, "maliyeti faydasından büyük olanı kes".
        public int VatRate { get; set; }
    }
}