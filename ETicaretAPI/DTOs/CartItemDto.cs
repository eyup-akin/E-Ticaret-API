namespace ETicaretAPI.DTOs
{
    public class CartItemDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal ProductPrice { get; set; }
        public int Quantity { get; set; }

        public string? ProductImageUrl { get; set; }    // ⭐ ana resim


        // ⭐ YENİ — bu ürün hâlâ satışta mı?
        //
        // Sepette bekleyen ürün, beklerken pasife düşmüş olabilir.
        // Mobil taraf bunu görüp satırı gri gösterecek ve "Siparişi
        // Tamamla"yı engelleyecek. Bu alan olmasaydı müşteri butona basar,
        // sunucudan anlaşılmaz bir hata alır ve neyin yanlış olduğunu
        // bilemezdi.
        public bool IsActive { get; set; }

        // ⭐ YENİ (5.4) — MÜŞTERİ BU ÜRÜNÜ SEPETE ATARKEN HANGİ FİYATI GÖRDÜ?
        //
        // ⚠️ BU BİR DONDURMA DEĞİL, BİR TANIK.
        // OrderItem.UnitPrice bağlayıcıdır ("müşteri bu tutarı ödedi").
        // Bu alan bağlayıcı değil; sipariş HER HÂLÜKÂRDA güncel
        // fiyattan oluşuyor. Tek işi müşteriye "sen bunu 100'e
        // görmüştün, şimdi 120" diyebilmek.
        //
        // Null = bu satır alan eklenmeden önce sepete girmiş.
        // Bilinmeyen bir geçmişle kıyaslama yapılamaz → uyarı yok.
        public decimal? EklenmeFiyati { get; set; }

        // ⭐ YENİ (5.4) — türetilmiş alanlar.
        //
        // ⚠️ NEDEN CONTROLLER'DA DEĞİL, BURADA?
        // "Fiyat değişti mi?" sorusunu sepet ucu da soruyor, sipariş
        // onay akışı da soracak. İki yere kopyalansaydı biri
        // "!=", diğeri ">" olarak yazılabilir ve aynı sepet iki
        // ekranda farklı uyarı gösterebilirdi. Kural tek yerde.
        //
        // ⚠️ SET'İ YOK — bilerek.
        // Bunlar veritabanından okunan değil, EklenmeFiyati ile
        // ProductPrice'tan HESAPLANAN değerler. Set'lenebilir
        // olsalardı biri elle yanlış doldurabilirdi.
        public bool FiyatDegisti =>
            EklenmeFiyati.HasValue && EklenmeFiyati.Value != ProductPrice;

        // Pozitif = fiyat ARTTI (müşteri aleyhine)
        // Negatif = fiyat DÜŞTÜ (müşteri lehine)
        //
        // İkisini tek alanda tutuyoruz; işaret zaten yönü söylüyor.
        // Ayrı "arttiMi" alanı eklemek, farkın işaretiyle çelişebilecek
        // ikinci bir doğruluk kaynağı yaratırdı.
        public decimal? FiyatFarki =>
            EklenmeFiyati.HasValue ? ProductPrice - EklenmeFiyati.Value : null;
    }
}