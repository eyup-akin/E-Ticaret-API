namespace ETicaretAPI.Models
{
    // ============================================================
    //  STOK BİLDİRİMİ — "stoka gelince haber ver"
    //
    //  Tükenmiş bir ürün için müşterinin bıraktığı istek.
    //  Ürün tekrar stoğa girince e-posta gidiyor.
    // ============================================================
    public class StockAlert
    {
        public int Id { get; set; }

        public int ProductId { get; set; }
        public int UserId { get; set; }

        public DateTime CreatedAt { get; set; }

        // ⭐ BİLDİRİM GÖNDERİLDİ Mİ?
        //
        // null  = bekliyor, ürün hâlâ stokta değil (ya da henüz taranmadı)
        // dolu  = bildirim gönderildi, bu kayıt kapandı
        //
        // ⚠️ Neden ayrı bir "GonderildiMi" bool'u DEĞİL?
        // Tarih hem "gönderildi mi?" sorusuna hem "ne zaman?" sorusuna
        // cevap veriyor. bool + tarih ikilisi tutsaydık ikisi
        // birbiriyle çelişebilirdi (bool true ama tarih boş gibi).
        // Tek alan, çelişki imkânsız.
        //
        // ⚠️ Kayıt SİLİNMİYOR, damgalanıyor. Silseydik "bu müşteriye
        // haber verdik mi?" sorusunun cevabı kaybolurdu ve aynı
        // müşteriye ikinci kez mail gitme riski doğardı.
        public DateTime? NotifiedAt { get; set; }
    }
}
