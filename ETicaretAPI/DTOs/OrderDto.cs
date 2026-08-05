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
    }

    public class OrderItemDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }
}