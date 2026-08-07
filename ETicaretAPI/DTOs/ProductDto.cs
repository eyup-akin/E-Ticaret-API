namespace ETicaretAPI.DTOs
{
    public class ProductDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }

        // ⭐ DEĞİŞTİ — artık nullable ve MÜŞTERİYE GÖNDERİLMİYOR.
        //
        // ⚠️ ESKİDEN HAM STOK HERKESE GİDİYORDU.
        //
        // Mobil ekranda "Stokta 3 adet var" yazıyordu ve JSON'da tam
        // sayı vardı. Bu iki sorun doğuruyordu:
        //
        //   1) TİCARİ: Rakip, her ürünün stoğunu günlük çekip satış
        //      hızını hesaplayabilir. Cost'u gizlemekle aynı gerekçe.
        //   2) PSİKOLOJİK: "Stokta 847 adet var" aciliyeti öldürüyor;
        //      "Son 3 ürün" satın almayı hızlandırıyor.
        //
        // Ekranda gizlemek YETMEZ — JSON'da giden her şey herkese
        // açıktır. Alanın kendisi boşaltılıyor.
        //
        // Admin isteklerinde dolu gelir: panelin envanter yönetmek
        // için gerçek sayıya ihtiyacı var.
        public int? Stock { get; set; }

        // ⭐ YENİ — MÜŞTERİYE GİDEN TÜRETİLMİŞ STOK BİLGİSİ
        //
        // "yok" | "az" | "var"
        //
        // ⚠️ Neden ham sayı yerine üç durum?
        // Müşterinin kararı için gereken bilgi bu üçü: alabilir mi
        // (yok/var), acele etmeli mi (az). Tam sayı bundan fazlasını
        // söylüyor ve fazlası ticari bilgi.
        //
        // Eşik MagazaAyarlari.StokAzEsigi'nden geliyor — panel, rapor
        // ve mobil aynı sayıyı kullanıyor.
        public string StokDurumu { get; set; } = "var";

        // Yalnızca StokDurumu == "az" iken dolu, diğer durumlarda null.
        //
        // ⚠️ Neden her durumda gönderilmiyor?
        // "var" durumunda göndermek, gizlemeye çalıştığımız ham sayıyı
        // geri sızdırırdı. "yok" durumunda ise zaten 0 — ayrıca
        // söylemenin bilgi değeri yok.
        //
        // Mobil bunu "Son 3 ürün" yazısında ve adet kontrolünün üst
        // sınırında kullanıyor.
        public int? KalanAdet { get; set; }

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


        // ⭐ YENİ (5.5) — bu müşterinin bekleyen stok bildirimi var mı?
        //
        // ⚠️ SADECE DETAY UCUNDA DOLU, listede her zaman false.
        // Butonun yaşadığı tek yer ürün detay sayfası; listede
        // doldurmak sayfa başına fazladan bir sorgu demekti.
        //
        // ⚠️ Misafirde de false — kimlik yoksa "kimin talebi"
        // sorusunun cevabı da yok. Mobil misafiri zaten giriş
        // ekranına yönlendiriyor.
        public bool StokBildirimiVar { get; set; }


        // ⭐ YENİ (4.8) — ürün arşivlendi mi?
        //
        // Tüketicisi yalnızca admin paneli: listede rozet, butonun
        // "Sil" mi "Arşivle" mi olacağı kararı.
        //
        // Müşteriye giden cevaplarda her zaman false — arşivli ürün
        // zaten müşteri listesine hiç girmiyor. IsActive'deki
        // gerekçenin aynısı: gizlemenin koruduğu bir şey yok,
        // gereksiz özel durum yaratmış oluruz.
        public bool ArsivlendiMi { get; set; }


        // ⭐ YENİ (4.8) — bu ürün FİZİKSEL olarak silinebilir mi?
        //
        // false = ürünün işlem geçmişi var (sipariş kalemi, yorum ya
        // da stok hareketi). Panel bu üründe "Sil" yerine "Arşivle"
        // gösteriyor.
        //
        // ⚠️ BU BİR KOLAYLIK, KURAL DEĞİL.
        // Asıl kilit DELETE ucunda: geçmişi olan ürün 409 ile
        // reddediliyor. Buradaki alan yalnızca adminin basamayacağı
        // bir butona bakmasını önlüyor. Liste yüklendikten sonra
        // ürüne sipariş gelirse bu değer bayatlar ve sunucu yine de
        // reddeder — doğru davranış.
        //
        // ⚠️ Yalnızca admin isteklerinde dolduruluyor; müşteriye
        // giden cevaplarda her zaman false. Müşterinin silme diye bir
        // eylemi yok, hesaplamak boşuna sorgu olurdu.
        public bool SilinebilirMi { get; set; }
    }
}