namespace ETicaretAPI.Services
{
    // TEK DOĞRU KAYNAK — Excel ürün sütunlarının tanımı.
    //
    // Hem içe aktarma (IceAktarmaServisi) hem şablon üretimi
    // (ImportsController) buradan okur. Bir sütunun adını burada
    // değiştirince ikisi birden değişir — asla ayrışamazlar.
    public class UrunKolonu
    {
        // Şablonda başlık olarak YAZILACAK ad (kullanıcıya gösterilen)
        public string BaslikAdi { get; set; } = string.Empty;

        // İçe aktarmanın TANIYACAĞI tüm adlar (küçük harf).
        // İlki genelde BaslikAdi'nın küçük hali olur.
        public string[] KabulEdilenAdlar { get; set; } = System.Array.Empty<string>();

        // Bu sütun boş bırakılamaz mı?
        public bool Zorunlu { get; set; }

        // Şablonun "Açıklama" sayfasında görünecek yardım metni
        public string Aciklama { get; set; } = string.Empty;

        // Şablona konacak örnek değer (dolu örnek satır için)
        public string OrnekDeger { get; set; } = string.Empty;
    }

    public static class UrunKolonlari
    {
        // Sütunların TANIMI ve SIRASI. Şablon bu sıraya göre üretilir.
        public static readonly UrunKolonu[] Hepsi = new[]
        {
            new UrunKolonu
            {
                BaslikAdi = "Barkod",
                KabulEdilenAdlar = new[] { "barkod", "barcode" },
                Zorunlu = true,
                Aciklama = "Ürünün benzersiz barkod numarası. Aynı barkod iki kez eklenemez.",
                OrnekDeger = "8690000000001"
            },
            new UrunKolonu
            {
                BaslikAdi = "Ürün Adı",
                KabulEdilenAdlar = new[] { "urun adi", "ürün adı", "ad", "isim", "name" },
                Zorunlu = true,
                Aciklama = "Ürünün görünen adı.",
                OrnekDeger = "Pamuklu Tişört"
            },
            new UrunKolonu
            {
                BaslikAdi = "Fiyat",
                KabulEdilenAdlar = new[] { "fiyat", "price" },
                Zorunlu = true,
                Aciklama = "Satış fiyatı. Ondalık için virgül veya nokta kullanılabilir (199,90).",
                OrnekDeger = "199,90"
            },
            new UrunKolonu
            {
                BaslikAdi = "Kategori",
                KabulEdilenAdlar = new[] { "kategori", "category" },
                Zorunlu = true,
                Aciklama = "Kategori adı. Sistemde yoksa otomatik oluşturulur.",
                OrnekDeger = "Giyim"
            },
            // ⭐ YENİ (B1 tamamlama, 2026-08-12)
            //
            // ⚠️ Fiyatın HEMEN ARDINDAN geliyor: Excel'i dolduran kişi
            // iki fiyatı yan yana görüp karşılaştırabilsin. Sona
            // koysaydık "hangi fiyat neydi" diye başa dönmek gerekirdi.
            new UrunKolonu
            {
                BaslikAdi = "Eski Fiyat",
                KabulEdilenAdlar = new[]
                {
                    "eski fiyat", "eskifiyat", "indirim oncesi fiyat",
                    "indirim öncesi fiyat", "old price", "oldprice", "list price"
                },
                Zorunlu = false,
                Aciklama = "İndirim ÖNCESİ fiyat (isteğe bağlı). Satış fiyatından " +
                           "büyük olmalı; değilse yok sayılır ve ürün indirimsiz kaydedilir.",
                OrnekDeger = "249,90"
            },
            new UrunKolonu
            {
                BaslikAdi = "Maliyet",
                KabulEdilenAdlar = new[] { "maliyet", "cost" },
                Zorunlu = false,
                Aciklama = "Ürünün alış maliyeti (isteğe bağlı). Kâr raporlarında kullanılır.",
                OrnekDeger = "120,00"
            },
            new UrunKolonu
            {
                BaslikAdi = "Stok",
                KabulEdilenAdlar = new[] { "stok", "stock", "adet" },
                Zorunlu = false,
                Aciklama = "Başlangıç stok adedi (isteğe bağlı). Boşsa 0 kabul edilir.",
                OrnekDeger = "50"
            },
            new UrunKolonu
            {
                BaslikAdi = "KDV Oranı",
                KabulEdilenAdlar = new[]
                {
                    "kdv orani", "kdv oranı", "kdv", "vat", "vatrate", "vat rate"
                },
                Zorunlu = false,
                Aciklama = "KDV oranı: 1, 10 veya 20 (isteğe bağlı). " +
                           "Boşsa 20 kabul edilir. Fiyat KDV DAHİL girilmelidir.",
                OrnekDeger = "20"
            },
            new UrunKolonu
            {
                BaslikAdi = "Açıklama",
                KabulEdilenAdlar = new[]
                {
                    "aciklama", "açıklama", "detay", "description"
                },
                Zorunlu = false,
                Aciklama = "Ürün açıklaması (isteğe bağlı). En fazla 2000 karakter.",
                OrnekDeger = "%100 pamuk, çift dikişli, 30 derecede yıkanabilir."
            },
            new UrunKolonu
            {
                BaslikAdi = "Resim",
                KabulEdilenAdlar = new[]
                {
                    "resim", "resimler", "gorsel", "görsel", "gorseller", "görseller",
                    "resim url", "görsel url", "gorsel url",
                    "image", "images", "image url", "url"
                },
                Zorunlu = false,
                Aciklama = "Resim linkleri (isteğe bağlı). Birden fazlaysa ; veya | ile ayır. En fazla 8 adet.",
                OrnekDeger = "https://ornek.com/tisort.jpg"
            }
        };
    }
}