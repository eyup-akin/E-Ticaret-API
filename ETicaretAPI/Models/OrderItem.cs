namespace ETicaretAPI.Models
{
    public class OrderItem
    {
        public int Id { get; set; }

        public int OrderId { get; set; }    // Bağlı olduğu sipariş
        public int ProductId { get; set; }  // Sipariş edilen ürün
        public int Quantity { get; set; }   // Adet

        // Sipariş anındaki fiyat — sonradan ürün fiyatı değişse bile bu sabit kalır
        public decimal UnitPrice { get; set; }

        // ⭐ YENİ — DONDURULMUŞ ÜRÜN ADI
        //
        // Neden ProductId yetmiyor?
        // ProductId "hangi ürün" sorusunu cevaplıyor ama "o gün bu ürünün
        // adı neydi" sorusunu cevaplamıyor. İkisi farklı sorular:
        //
        //   ProductId  → "bu kalem hangi ürüne bağlı"  (canlı ilişki)
        //   ProductName→ "müşteri neyi sipariş etti"    (donmuş gerçek)
        //
        // Admin ürün adını düzeltirse (yanlış yazılmıştı, düzeltti)
        // eski siparişler de yeni adı gösteriyordu. Müşterinin elindeki
        // sipariş onayı ile paneldeki kayıt birbirini tutmuyordu.
        //
        // Ayrıca ürün silinirse JOIN eşleşmez ve kalem listeden komple
        // DÜŞERDİ — sipariş toplamı 500 TL görünüp içinde 2 kalem
        // yerine 1 kalem kalırdı.
        //
        // UnitPrice'ı donduruyorduk ama adı dondurmuyorduk; kayıt yarı
        // donmuş yarı canlıydı. Bu tutarsızlığı kapatıyor.
        //
        // Neden nullable değil?
        // Ürünün adı her zaman vardır. "Adı bilinmiyor" diye bir durum
        // yok — nullable yapsaydık her okuma yerinde null kontrolü
        // gerekirdi ve hiçbiri gerçekten işe yaramazdı.
        public string ProductName { get; set; } = string.Empty;

        // ⭐ YENİ — DONDURULMUŞ BİRİM MALİYET
        //
        // Kâr = (UnitPrice − UnitCost) × Quantity
        //
        // Maliyeti Product.Cost'tan CANLI okusaydık, tedarikçi zam
        // yaptığı gün GEÇMİŞ AYLARIN kâr raporu da değişirdi.
        // Ocak'ta 50 TL kâr gösteren sipariş, Şubat'ta maliyet
        // güncellendiği için 10 TL kâr göstermeye başlardı.
        //
        // Hiçbir hata mesajı çıkmaz, hiçbir şey patlamaz — rapor sadece
        // yalan söyler. Bu yüzden UnitPrice ile aynı muameleyi görüyor.
        //
        // Neden nullable (decimal?)?
        // İki sebep:
        //   1) Product.Cost'un kendisi nullable — maliyeti hiç girilmemiş
        //      ürünler var. Kopyalanacak değer yoksa null yazılır.
        //   2) Bu alan eklenmeden önceki siparişlerde null kalacak.
        //      Bilerek: bugünkü maliyeti geçmişe yazmak UYDURMA veri
        //      üretirdi. Rapor "maliyet bilinmiyor" demeli, uydurulmuş
        //      bir kâr rakamı göstermemeli.
        public decimal? UnitCost { get; set; }

        // ⭐ YENİ — DONDURULMUŞ KDV ORANI
        //
        // Sipariş anında Product.VatRate'ten kopyalanır.
        //
        // NEDEN DONDURULUYOR?
        // KDV oranları YASAYLA değişir ve geçmişe dönük uygulanmaz.
        // Oranı Product'tan canlı okusaydık, oran %20'den %10'a indiği
        // gün geçmiş faturaların hepsi yeni oranı gösterirdi. Müşterinin
        // elindeki fiş ile sistemdeki kayıt tutmazdı — ve bu bir muhasebe
        // sorunudur, kozmetik bir tutarsızlık değil.
        //
        // UnitPrice, ProductName ve UnitCost ile aynı muamele.
        //
        // ⚠️ NEDEN nullable (int?) VE MIGRATION'DA DOLDURULMUYOR?
        //
        // Buradaki ayrım, ProductName (dolduruldu) ile UnitCost
        // (doldurulmadı) arasındaki ayrımın aynısı:
        //
        //   Product.VatRate  → BUGÜN hakkında bir iddia. "Bu ürün genel
        //                      orana tabi" demek güvenli, doğrulanabilir.
        //   OrderItem.VatRate→ GEÇMİŞ hakkında bir iddia. O siparişte
        //                      hangi oranın uygulandığını gerçekten
        //                      BİLMİYORUZ — sistemde KDV kaydı yoktu.
        //
        // 0 yazsaydık ekranda "KDV (%0): 0,00 TL" çıkardı. Bu eksik bir
        // bilgi değil, YANLIŞ bir bilgi olurdu — o siparişlerde KDV
        // alınmadığını iddia ederdi. Null bırakınca eski siparişlerde
        // KDV satırı hiç çizilmiyor.
        //
        // "Yanlış sayı, eksik sayıdan tehlikelidir."
        public int? VatRate { get; set; }
    }
}