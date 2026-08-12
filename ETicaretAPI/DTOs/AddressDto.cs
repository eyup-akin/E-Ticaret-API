namespace ETicaretAPI.DTOs
{
    public class AddressDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string FullAddress { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;

        // ⭐ DEĞİŞTİ (4.9) — hangi numara seçili.
        // Adres düzenleme formu bunu okuyup listede işaretliyor.
        public int? PhoneId { get; set; }

        // ⭐ DEĞİŞTİ (4.9) — gösterim biçimi ("0552 808 31 29").
        //
        // ⚠️ Alan adı `Phone` KORUNDU. İçeriği artık canlı bir
        // JOIN'den geliyor ama istemci açısından anlamı aynı: "bu
        // adres için aranacak numara". Adını değiştirmek üç ekranı
        // (mobil adres kartı, admin sipariş dökümü, kargo etiketi)
        // hiçbir kazanç olmadan kırardı.
        //
        // ⚠️ NULLABLE OLDU. Numara silinmişse adres telefonsuz
        // kalıyor ve ekranda "—" görünüyor. Boş string döndürmek
        // "numara yok" ile "numara boş" ayrımını yok ederdi.
        public string? Phone { get; set; }
    }
}
