namespace ETicaretAPI.Models
{
    // ⭐ YENİ — HIZLI SİPARİŞ (kaydedilmiş sipariş)
    //
    // Müşteri bir siparişi "hızlı siparişlerime kaydet" diyerek
    // işaretliyor; sonradan tek dokunuşla aynı ürünleri sepete
    // atabilecek.
    //
    // ⚠️ SİPARİŞİN İÇERİĞİ BURAYA KOPYALANMIYOR — sadece işaret.
    //
    // Kalemleri (ürün, adet, fiyat) buraya kopyalamak akla geliyor ama
    // yanlış olurdu:
    //   • Sipariş zaten OrderItems'ta duruyor ve orası DONDURULMUŞ
    //     veri — ikinci bir kopya tutmak "aynı gerçek iki yerde"
    //     demekti ve ikisi bir gün ayrışırdı
    //   • Tekrar sipariş akışı (POST /orders/{id}/tekrarla) zaten
    //     sipariş id'sinden çalışıyor; kopya olsa onu da beslemek
    //     için ikinci bir yol yazmak gerekirdi
    //
    // Yani bu tablo bir İŞARET tablosu: "şu kullanıcı şu siparişi
    // kaydetti". Favorites ile aynı şekil.
    //
    // ⚠️ NEDEN CİHAZDA DEĞİL DE SUNUCUDA?
    // Son gezilen ürünler (sonGezilenler.js) cihazda tutuluyor çünkü
    // o bir davranış izi — kaybolması kimseyi üzmez. Hızlı sipariş ise
    // müşterinin BİLEREK kaydettiği bir liste; uygulama silinince ya da
    // telefon değişince kaybolması kötü bir sürpriz olurdu. Favoriler
    // de aynı gerekçeyle sunucuda.
    public class HizliSiparis
    {
        public int Id { get; set; }

        // ⚠️ Sipariş üzerinden de bulunabilirdi (Order.UserId) ama
        // burada AYRICA tutuluyor. Sebep: sahiplik kontrolü ve
        // benzersizlik kısıtı bu kolona ihtiyaç duyuyor —
        // (UserId, OrderId) bileşik unique index'i olmadan aynı
        // sipariş iki kez kaydedilebilirdi.
        public int UserId { get; set; }

        public int OrderId { get; set; }

        // ⭐ YENİ — SİPARİŞİN İÇERİK İMZASI
        //
        // ⚠️ NEDEN GEREKTİ?
        //
        // (UserId, OrderId) benzersizliği aynı SİPARİŞİN iki kez
        // kaydedilmesini engelliyordu ama aynı İÇERİĞİN iki kez
        // kaydedilmesini engellemiyordu. Müşteri zeytinyağı sipariş
        // edip kaydediyor, ertesi gün yine zeytinyağı sipariş edip
        // onu da kaydediyordu — listede birbirinin aynı iki satır.
        //
        // ⚠️ KİMLİK ARTIK "HANGİ SİPARİŞ" DEĞİL, "NE VAR İÇİNDE".
        //
        // İMZA TARİFİ (değiştirirsen mevcut satırlar geçersiz olur):
        //   1. Kalemleri ProductId'ye göre grupla, adetleri topla
        //      (aynı ürün iki satırda olabiliyor)
        //   2. ProductId'ye göre artan sırala
        //   3. "productId x adet" parçalarını "|" ile birleştir
        //      → "67x3|70x1|72x2"
        //   4. UTF-8 baytlarının SHA-256'sını küçük harf hex olarak yaz
        //
        // ⚠️ NEDEN HAM METİN DEĞİL, HASH?
        // Ham metin ürün sayısıyla büyüyor ve indeks anahtarının üst
        // sınırı var (1700 bayt). Hash sabit 64 karakter — indeks her
        // sipariş boyutunda aynı maliyette.
        //
        // ⚠️ ADET İMZAYA DAHİL. {zeytinyağı ×1} ile {zeytinyağı ×3}
        // FARKLI sayılıyor: listede "1 adet" ve "3 adet" olarak ayırt
        // edilebiliyorlar, yani mükerrer görünmüyorlar. Adeti dışarıda
        // bıraksaydık müşteri farklı miktardaki bir siparişi hiç
        // kaydedemezdi.
        public string IcerikImzasi { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
