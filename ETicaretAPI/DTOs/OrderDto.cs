using System;
using System.Collections.Generic;

namespace ETicaretAPI.DTOs
{
    public class OrderDto
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; }

        public string ShippingFullName { get; set; }
        public string ShippingTitle { get; set; }
        public string ShippingCity { get; set; }
        public string ShippingFullAddress { get; set; }
        public string ShippingPhone { get; set; }

        public decimal SubTotal { get; set; }
        public string? CouponCode { get; set; }
        public decimal DiscountAmount { get; set; }

        // ⭐ YENİ — KARGO ÜCRETİ (dondurulmuş)
        //
        // Siparişin verildiği andaki kargo ücreti. Mağaza ücreti
        // sonradan değiştirse bile bu sipariş ne ödendiyse onu
        // gösterir.
        //
        // 0 olabilir ve bunun İKİ anlamı var:
        //   • ücretsiz kargo eşiği aşılmış
        //   • mağaza hiç kargo ücreti almıyor
        //
        // Ekranda "Kargo: Ücretsiz" yazmak ikisinde de doğru,
        // ayırt etmeye gerek yok.
        public decimal ShippingCost { get; set; }

        public decimal Total { get; set; }

        public string Status { get; set; }
        public string PaymentStatus { get; set; }
        public string CardLast4 { get; set; }

        public DateTime CreatedAt { get; set; }
        public string? CancelReason { get; set; }
        public DateTime? CancelledAt { get; set; }

        public string? ShippingCompany { get; set; }
        public string? TrackingNumber { get; set; }
        public DateTime? ShippedAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public string? CustomerNote { get; set; }

        public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();

        // ⭐ YENİ — KDV DÖKÜMÜ
        //
        // ⚠️ BU ALANLAR TOPLAMA HİÇBİR ŞEY EKLEMEZ.
        //
        // Fiyatlar KDV dahil olduğu için Total zaten nihai tutar.
        // Buradaki değerler o tutarın İÇİNDEN ayrıştırılmış hali —
        // bilgilendirme amaçlı. Mobil bunları toplama katmamalı.
        //
        // Boş liste, dökümün gösterilmemesi gerektiği anlamına gelir
        // (bkz. HasVatBreakdown).
        public List<VatLineDto> VatLines { get; set; } = new List<VatLineDto>();

        public decimal TotalVatBase { get; set; }
        public decimal TotalVat { get; set; }

        // Döküm gösterilebilir mi?
        //
        // ⚠️ Bu alan neden var, neden ekran "VatLines.Count > 0" demiyor?
        //
        // KDV oranları bu özellik eklenmeden ÖNCEKİ siparişlerde null.
        // O siparişlerde hangi oranın uygulandığını bilmiyoruz ve KDV
        // satırı hiç çizilmemeli.
        //
        // Kararı sunucuda verip tek bir bool olarak göndermek, üç
        // ekranın aynı koşulu ayrı ayrı yazmasından güvenli: biri
        // yanlış yazarsa eski siparişte "KDV: 0,00 TL" görünür — yani
        // eksik değil YANLIŞ bilgi.
        public bool HasVatBreakdown { get; set; }
    }

    // ⭐ YENİ — tek bir KDV oranına ait satır.
    //
    // Neden liste? Bir sepette %1'lik gıda ile %20'lik elektronik
    // birlikte olabilir. "Siparişin KDV oranı" diye tek bir şey yok;
    // fatura oran bazında kesilir.
    public class VatLineDto
    {
        // Yüzde olarak oran (1, 10, 20)
        public int Rate { get; set; }

        // KDV hariç tutar
        public decimal NetAmount { get; set; }

        // Verginin kendisi
        public decimal VatAmount { get; set; }

        // KDV dahil tutar (NetAmount + VatAmount)
        public decimal GrossAmount { get; set; }
    }

    public class OrderItemDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }

        // ⭐ YENİ — o kalemin dondurulmuş KDV oranı.
        //
        // Nullable: bu alan eklenmeden önceki kalemlerde boş.
        // Ekran null ise oran bilgisini hiç göstermez.
        public int? VatRate { get; set; }

        // ⭐ YENİ (B1) — sipariş anında dondurulmuş indirim öncesi
        // fiyat. Eski siparişlerde null ve orada kazanç satırı hiç
        // çizilmiyor.
        public decimal? EskiFiyat { get; set; }
    }
}