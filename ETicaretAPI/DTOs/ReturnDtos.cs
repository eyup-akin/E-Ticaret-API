using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // ⭐ YENİ (Aşama 9) — İADE DTO'LARI

    // ---- YAZMA ----

    public class IadeTalepDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir sipariş seçmelisin!")]
        public int OrderId { get; set; }

        // ⚠️ null = siparişin tamamı. `int?` olduğu için [Range]
        // gerekmiyor: gönderilmezse null kalıyor, gönderilirse
        // controller sahipliğini ve siparişe ait olup olmadığını
        // sorgunun içinde kontrol ediyor.
        public int? OrderItemId { get; set; }

        [Required(ErrorMessage = "İade sebebi seçmelisin!")]
        public string Sebep { get; set; } = string.Empty;

        // ⚠️ Zorunlu DEĞİL: "bedene uymadı" kendi kendini anlatıyor.
        // Zorunlu yapsaydık müşteri anlamsız bir şey yazıp geçerdi.
        [StringLength(1000, ErrorMessage = "Açıklama en fazla 1000 karakter olabilir!")]
        public string? Aciklama { get; set; }
    }

    public class IadeKararDto
    {
        // ⚠️ Serbest durum metni DEĞİL: admin yalnızca "onayla" ya da
        // "reddet" diyor. Durum makinesinin geri kalanını (teslim
        // alındı, para iade edildi) ayrı uçlar yürütüyor — her adımın
        // kendi yan etkisi var ve tek bir "durumu şu yap" ucu o yan
        // etkileri atlanabilir hale getirirdi.
        public bool Onay { get; set; }

        // Yalnızca reddederken. ⚠️ "Reddedildi" tek başına bir cevap
        // değil; müşteri neden reddedildiğini görmeli.
        [StringLength(500, ErrorMessage = "Red nedeni en fazla 500 karakter olabilir!")]
        public string? RedNedeni { get; set; }
    }


    // ---- OKUMA ----

    public class IadeOzetDto
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string SiparisNo { get; set; } = string.Empty;

        // null = tüm sipariş
        public int? OrderItemId { get; set; }

        // ⚠️ Ürün adı `OrderItem.ProductName`'den (dondurulmuş):
        // ürün adı sonradan değişse bile iade kaydı ne iade
        // edildiğini söylemeye devam etmeli.
        public string? UrunAdi { get; set; }

        public string Sebep { get; set; } = string.Empty;
        public string? Aciklama { get; set; }
        public string Durum { get; set; } = string.Empty;

        public DateTime TalepTarihi { get; set; }
        public DateTime? KararTarihi { get; set; }
        public string? RedNedeni { get; set; }

        // ⚠️ İKİ AYRI TUTAR ALANI — ve bu bilinçli.
        //
        // `Tutar` HESAPLANMIŞ değer: "onaylanırsa bu kadar
        // ödenecek". Her istekte yeniden hesaplanıyor.
        //
        // `IadeTutari` DONDURULMUŞ değer: "gerçekten şu kadar
        // ödendi". Yalnızca para iade edildiyse dolu.
        //
        // Tek alana indirseydik "ödenecek" ile "ödendi" ayrımı
        // kaybolurdu ve hesap kuralı yarın değişince geçmiş
        // iadeler de değişmiş görünürdü.
        public decimal Tutar { get; set; }
        public decimal? IadeTutari { get; set; }

        // Admin listesinde dolu, müşteri listesinde null.
        public string? MusteriAdi { get; set; }
    }
}
