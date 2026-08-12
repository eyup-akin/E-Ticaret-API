namespace ETicaretAPI.Models
{
    // ============================================================
    //  ⭐ YENİ (Aşama 8) — DESTEK YAZIŞMASI
    //
    //  Bir talebin altındaki tek bir mesaj. Talep bir başlık,
    //  bunlar konuşmanın kendisi.
    // ============================================================
    public class SupportMessage
    {
        public int Id { get; set; }

        public int TicketId { get; set; }

        // Mesajı yazan kişi.
        public int GonderenUserId { get; set; }

        // ⚠️⚠️ BU ALAN TÜRETİLEBİLİR GİBİ GÖRÜNÜYOR AMA DEĞİL.
        //
        // "Gönderenin rolüne bakarız" demek cazip: `Users.Role`
        // zaten duruyor. Ama rol DEĞİŞEN bir şey — bugünün admini
        // yarın müşteri olabilir (ya da tam tersi, ki bu projede
        // admin başvurusuyla gerçekten oluyor). Rolden okusaydık o
        // gün BÜTÜN geçmiş yazışmalar yeniden etiketlenirdi:
        // adminin bir yıl önce yazdığı cevaplar müşteri mesajı gibi
        // görünür, konuşma tarafları yer değiştirirdi.
        //
        // Bu alan "o mesaj yazılırken kim, hangi sıfatla konuştu"
        // sorusunun DONMUŞ cevabı — `OrderItem.ProductName` ve
        // `Order.ShippingPhone` ile aynı prensip.
        //
        // ⚠️ Gönderen adı DONDURULMUYOR (yorumcu adında verilen
        // kararın aynısı): "bu kişi kim" sorusunun cevabı canlı
        // olmalı, adını değiştiren admin her yerde yeni adıyla
        // görünsün.
        public bool GonderenAdminMi { get; set; }

        public string Mesaj { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
