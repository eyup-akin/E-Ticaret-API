namespace ETicaretAPI.Models
{
    public class Address
    {
        public int Id { get; set; }

        // Adresin sahibi — User tablosuna bağlanır
        public int UserId { get; set; }

        public string Title { get; set; } = string.Empty;       // Ev, İş vb.
        public string FullAddress { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;

        // ⭐ DEĞİŞTİ (4.9) — alıcı telefonu artık METİN DEĞİL, REFERANS.
        //
        // Eskiden burada `string Phone` vardı ve numara adres başına
        // KOPYALANIYORDU. Müşteri numarasını değiştirdiğinde N adresi
        // tek tek düzeltmek zorunda kalıyordu — "tek doğru kaynak"
        // ilkesinin doğrudan ihlali.
        //
        // ⚠️ REFERANS EDİYOR, KOPYALAMIYOR. Numara Phone tablosunda
        // yaşıyor; adres yalnızca "bu teslimatta hangisi aransın?"
        // sorusunu cevaplıyor.
        //
        // ⚠️ NEDEN NULLABLE?
        // Müşteri bir numarayı silebilir. FK'da ON DELETE SET NULL
        // tanımlı: silinen numaraya bağlı adresler telefonsuz kalıyor
        // ve o adres sipariş sırasında yeniden numara seçilmesini
        // istiyor. Alternatifler daha kötüydü: adresi de silmek
        // (müşterinin adresini haber vermeden yok etmek) ya da
        // silmeyi engellemek (müşteriyi çıkmaza sokmak).
        //
        // ⚠️ Kolon nullable ama DTO'da EKLEME/DÜZENLEME İÇİN ZORUNLU.
        // Çelişki değil: "null OLABİLİR" ile "null OLARAK
        // yaratılabilir" farklı şeyler. Adres formu her zaman bir
        // numara seçtiriyor; null yalnızca sonradan silinmeyle oluşur.
        public int? PhoneId { get; set; }
    }
}