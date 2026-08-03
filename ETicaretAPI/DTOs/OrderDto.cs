namespace ETicaretAPI.DTOs
{
    public class OrderDto
    {
        public int Id { get; set; }                              // teknik anahtar (URL için)
        public string OrderNumber { get; set; } = string.Empty;   // ⭐ ekranda gösterilen numara

        public decimal Total { get; set; }
        public string Status { get; set; } = string.Empty;       // kargo durumu
        public string PaymentStatus { get; set; } = string.Empty; // ödeme durumu
        public string CardLast4 { get; set; } = string.Empty;

        // ⭐ Dondurulmuş teslimat adresi — sipariş anındaki hali
        public string ShippingFullName { get; set; } = string.Empty;
        public string ShippingTitle { get; set; } = string.Empty;
        public string ShippingCity { get; set; } = string.Empty;
        public string ShippingFullAddress { get; set; } = string.Empty;
        public string ShippingPhone { get; set; } = string.Empty;   // ⭐ YENİ

        // ⭐ KUPON — dondurulmuş indirim bilgisi
        public decimal SubTotal { get; set; }                        // indirimden önceki tutar
        public string CouponCode { get; set; } = string.Empty;       // boş = kupon yok
        public decimal DiscountAmount { get; set; }                  // 0 = indirim yok

        public DateTime CreatedAt { get; set; }        // sipariş tarihi
        public string? CancelReason { get; set; }      // iptal sebebi (null = iptal değil)
        public DateTime? CancelledAt { get; set; }     // iptal tarihi

        // ⭐ YENİ — KARGO TAKİP BİLGİLERİ
        //
        // Hepsi nullable ve bu doğrudan JSON'a yansıyor: sipariş henüz
        // kargoya verilmemişse mobil taraf { "trackingNumber": null }
        // görüyor ve "kargo kutusunu hiç çizme" kararını buradan veriyor.
        //
        // Alternatif, alan yoksa JSON'dan tamamen çıkarmaktı. Bunu
        // yapmadık: null gelmesi "biliyorum ve boş" demek, alanın hiç
        // olmaması "bilmiyorum" demek. İstemci ikisini ayırt edebilmeli.
        public string? ShippingCompany { get; set; }
        public string? TrackingNumber { get; set; }
        public DateTime? ShippedAt { get; set; }
        public DateTime? DeliveredAt { get; set; }

        // ⭐ YENİ — müşterinin sipariş notu.
        // Hem müşteri kendi siparişinde görsün (ne yazdığını hatırlasın)
        // hem admin kargo hazırlarken okusun.
        public string? CustomerNote { get; set; }

        public List<OrderItemDto> Items { get; set; } = new();

       
    }

    // Sipariş içindeki her ürün satırı
    public class OrderItemDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; } // dondurulmuş fiyat
    }
}