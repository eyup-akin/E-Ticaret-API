namespace ETicaretAPI.Models
{
    public class Review
    {
        public int Id { get; set; }

        public int ProductId { get; set; }   // yorum yapılan ürün
        public int UserId { get; set; }       // yorumu yapan kullanıcı

        public int Rating { get; set; }        // 1-5 yıldız
        public string Comment { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ⭐ YENİ — YORUM GİZLENDİ Mİ?
        //
        // Raporlarda düşük puanlı yorumları listeleyeceğiz; arasında
        // küfür/spam çıkarsa admin kaldırmak isteyecek.
        //
        // Neden silmek yerine gizlemek?
        //   • Silinen yorum denetlenemez — "bunu kim, neden sildi"
        //     sorusunun cevabı kalmaz
        //   • Yanlışlıkla silinen geri gelmez
        //   • Ürünün ortalama puanı sessizce yükselir, kimse sebebini
        //     bilmez
        // Product.IsActive'de uyguladığımız desenin aynısı: kayıt yerinde
        // durur, sadece görünürlükten çıkar.
        //
        // Neden IsVisible değil de IsHidden?
        // Bool alanın varsayılanı (false) "normal durumu" anlatmalı.
        // Yorumun normali GÖRÜNÜR olmak, gizlenmek istisnadır.
        // IsVisible deseydik varsayılanı true yapmamız ve migration'da
        // tüm eski satırlara true yazmamız gerekirdi. IsHidden ile
        // SQL Server'ın verdiği varsayılan 0 zaten doğru cevap —
        // backfill adımı komple ortadan kalkıyor.
        //
        // ⚠️ Gizli yorumlar ortalama puan hesabından da ÇIKARILACAK
        // (bu, 2.2'de endpoint'ler yazılırken yapılacak). Yorumu
        // gizleyip puanı bırakmak yarım iş olurdu.
        public bool IsHidden { get; set; } = false;
    }
}