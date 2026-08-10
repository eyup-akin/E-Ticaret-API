namespace ETicaretAPI.Models
{
    public class Coupon
    {
        public int Id { get; set; }

        // Kupon kodu — müşterinin yazacağı metin. BÜYÜK harfe normalize edilir.
        // Unique index var: aynı kod iki kez oluşturulamaz.
        public string Code { get; set; } = string.Empty;

        // Admin için açıklama — "Yeni yıl kampanyası" gibi. Müşteri görmez.
        public string Description { get; set; } = string.Empty;

        // "yuzde" veya "tutar"
        public string DiscountType { get; set; } = "yuzde";

        // yuzde ise 10 = %10 indirim
        // tutar ise 50 = 50 TL indirim
        public decimal DiscountValue { get; set; }

        // Sepet bu tutarın altındaysa kupon geçmez. 0 = sınır yok.
        public decimal MinOrderAmount { get; set; }

        // Yüzdeli kuponlarda indirim tavanı.
        // Örn: %20 indirim ama en fazla 200 TL.
        // null = tavan yok. Tutar tipinde anlamsız, kullanılmaz.
        public decimal? MaxDiscountAmount { get; set; }

        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }

        // Toplam kaç kez kullanılabilir. null = sınırsız.
        public int? UsageLimit { get; set; }

        // Bir kullanıcı kaç kez kullanabilir. Genelde 1.
        public int UsageLimitPerUser { get; set; } = 1;

        // Şu ana kadar kaç kez kullanıldı.
        // CouponUsage'dan sayılabilirdi ama her doğrulamada COUNT atmak
        // pahalı olurdu — sayacı burada tutuyoruz.
        public int UsedCount { get; set; }

        // Admin elle kapatabilsin. Tarih geçerli olsa bile pasifse çalışmaz.
        public bool IsActive { get; set; } = true;

        // Sadece belirli bir kategoride geçerli olsun mu? null = tüm ürünler.
        public int? CategoryId { get; set; }

        // ⭐ YENİ (B1) — İNDİRİMLİ ÜRÜNLERDE GEÇERLİ Mİ?
        //
        // false ise kupon, EskiFiyat'ı dolu olan (yani zaten indirimli)
        // kalemleri indirim matrahından düşürüyor. "İndirim üstüne
        // indirim olmasın" diyen kampanyalar için.
        //
        // ⚠️ Bu, CategoryId ile AYNI ŞEKİLDE çalışıyor: ikisi de
        // "matraha hangi kalemler girer" sorusunu daraltıyor ve
        // birlikte kullanılabiliyorlar. Yeni bir hesap yolu açmıyor,
        // var olanı süzüyor — o yüzden KuponServisi'nde ek bir dal
        // değil, mevcut filtrenin devamı.
        //
        // ⚠️ MİNİMUM SEPET TUTARI BUNDAN ETKİLENMİYOR.
        // "200 TL üzeri alışverişte geçerli" derken kastedilen toplam
        // alışveriş; kuponun hangi kalemlere işlediği ayrı bir soru.
        // Kategori filtresinde de aynı karar verilmişti.
        //
        // ⚠️ = true varsayılanı ve ESKİ KUPONLAR true İLE DOLUYOR.
        // Bu UYDURMA DEĞİL: bugüne kadar indirimli ürün diye bir
        // kavram yoktu, yani mevcut kuponlar fiilen her ürüne
        // işliyordu. true, o davranışı aynen koruyor.
        public bool IndirimliUrunlerdeGecerli { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int CreatedByUserId { get; set; }
    }
}