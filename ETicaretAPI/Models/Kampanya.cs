namespace ETicaretAPI.Models
{
    // ⭐ YENİ (B2) — KAMPANYA / BANNER
    //
    // Ana sayfanın en üstündeki afiş şeridi ve arkasındaki detay
    // ekranı bu tablodan besleniyor. Önceden mobilde yerel bir
    // dosyada (services/kampanyalar.js) sabit duruyordu; görsel
    // değiştirmek APK almayı gerektiriyordu.
    //
    // ⚠️ NEDEN "Kampanya", NEDEN "Banner" DEĞİL?
    //
    // Banner bir GÖRSEL; kampanya arkasındaki iş. Müşteri afişe
    // basınca banner'ı değil kampanyayı okuyor: koşullar, süre,
    // kupon kodları. Adı "Banner" koysaydık, görselsiz bir kampanya
    // girişi eklendiği gün ad yalan söylerdi. (Aynı gerekçe mobil
    // tarafta da yazılıydı.)
    public class Kampanya
    {
        public int Id { get; set; }

        // Afişin üstünde değil, DETAY ekranında görünen başlık.
        // Görselin kendi yazısı ayrı; burası ekran okuyucunun da
        // okuduğu metin.
        public string Baslik { get; set; } = string.Empty;

        public string KisaAciklama { get; set; } = string.Empty;

        // "30 Kasım'a kadar", "Üyeliğinden itibaren 30 gün" gibi.
        //
        // ⚠️ SERBEST METİN, TARİH DEĞİL — bilinçli. Yukarıdaki ikinci
        // örnek bir takvim tarihine çevrilemiyor (her müşteri için
        // farklı). Tarih alanı koyup bir de bu metni tutsaydık aynı
        // gerçeğin iki kaynağı olurdu ve biri güncellenip diğeri
        // unutulurdu.
        public string BitisMetni { get; set; } = string.Empty;

        public string Aciklama { get; set; } = string.Empty;

        // "/uploads/kampanyalar/a3f9c1.jpg"
        public string GorselUrl { get; set; } = string.Empty;

        // ⚠️ KUPON KODLARI ve KOŞULLAR SATIR SATIR TEK KOLONDA.
        //
        // İkisi de yalnızca EKRANDA GÖSTERİLEN listeler: hiçbir sorgu
        // bunların içinde arama yapmıyor, hiçbir rapor bunları
        // gruplamıyor. Ayrı birer tablo açmak üç tablo, üç migration
        // ve her okumada iki join demekti — karşılığında kazanılan
        // tek şey "doğru görünen" bir şema olurdu.
        //
        // ⚠️ Kupon kodları KAYDEDİLİRKEN doğrulanıyor (Coupons
        // tablosunda var mı). Yani metin serbest ama içerik değil:
        // müşteriye tutmayacağımız bir indirim sözü verilmiyor.
        //
        // ⚠️ Ayraç \n. Virgül seçseydik koşul metinlerinde geçen
        // virgüller satırı ikiye bölerdi.
        public string KuponKodlari { get; set; } = string.Empty;
        public string Kosullar { get; set; } = string.Empty;

        // Şeritteki sıra. Küçük olan önce.
        //
        // ⚠️ Sıra alanı olmasaydı tek sıralama ölçütü Id kalırdı ve
        // yöneticinin "şunu başa al" demesinin tek yolu kampanyayı
        // silip yeniden eklemek olurdu.
        public int Sira { get; set; }

        // Yayında mı? Silmeden gizlemenin yolu.
        //
        // ⚠️ Sezonluk afişler siliniyor değil kapatılıyor: gelecek
        // yıl aynı metin ve kupon listesiyle geri açılabilsin.
        public bool AktifMi { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
