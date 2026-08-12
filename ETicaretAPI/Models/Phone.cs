namespace ETicaretAPI.Models
{
    // ⭐ YENİ (4.9) — MÜŞTERİNİN TELEFON DEFTERİ
    //
    // ⚠️ BU TABLONUN GEREKÇESİ "BİRDEN FAZLA NUMARA" DEĞİL.
    // Çoklu numara zaten mümkündü: her adresin kendi telefonu vardı.
    // Tabloyu haklı çıkaran üç şey başka:
    //
    //   1) Users'ta telefon HİÇ YOKTU. Adresi olmayan müşterinin
    //      numarası da yoktu — SMS doğrulama, kargo SMS'i ve hesap
    //      kurtarma imkânsızdı.
    //   2) Numara adres başına KOPYALANIYORDU. Müşteri numarasını
    //      değiştirince N adresi tek tek düzeltmek zorundaydı.
    //   3) Doğrulama durumunun yeri yoktu. "Bu numara SMS ile
    //      doğrulandı" bilgisi bir string kolona sığmaz; adres
    //      başına doğrulama zaten anlamsız olurdu.
    //
    // ⚠️ ADRES TELEFONU İLE HESAP TELEFONU AYNI ŞEY DEĞİLDİR — ve bu
    // tablo ikisini de taşıyor, çünkü ikisi de "bu kişinin bir
    // numarası". Fark KULLANIMDA:
    //   • Adres, buradaki bir satırı REFERANS eder → "kurye BU
    //     teslimat için kimi arasın?" (anneye hediye gönderiyorsan
    //     annenin numarası)
    //   • VarsayilanMi olan satır hesap telefonudur → "hesap
    //     sahibine nasıl ulaşırız?" (OTP, bildirim, kurtarma)
    //
    // ⚠️ Order.ShippingPhone'a FK VERİLMEDİ — bilinçli. Sipariş
    // telefonu dondurulmuş bir KOPYA olarak kalıyor. FK verseydik
    // müşteri numarasını sildiğinde iki yıl önceki siparişin
    // telefonu da kaybolur ya da bambaşka bir numarayı gösterirdi;
    // "o gün kime gönderdik" sorusunun cevabı bozulurdu. Aynı
    // gerekçe ShippingCity, CardLast4 ve OrderItem.ProductName için
    // de geçerli — canlı varlık + sipariş anında dondurma.
    public class Phone
    {
        public int Id { get; set; }

        // Numaranın sahibi — User tablosuna bağlanır
        public int UserId { get; set; }

        // ⚠️ NORMALİZE EDİLMİŞ HALİ SAKLANIR: sadece 10 hane, alan
        // kodu dahil, başında sıfır YOK ("5528083129").
        //
        // Neden ham girdi saklanmıyor? "0532 123 45 67" ile
        // "+905321234567" aynı numaradır ama farklı iki metindir.
        // Ham saklasaydık benzersizlik indeksi ikisini iki ayrı
        // numara sanardı ve müşteri aynı numarayı defalarca
        // kaydedebilirdi.
        //
        // Ekranda gösterilecek biçim (0532 123 45 67) buradan
        // TÜRETİLİYOR (TelefonBicimi.Goster) — saklanmıyor. İki
        // yerde yaşayan bir gerçek er ya da geç ikiye ayrılır.
        public string Numara { get; set; } = string.Empty;

        // "Cep", "İş", "Annem" — hangi numara olduğunu müşteri
        // kendisi adlandırır. Adres başlığındaki (Ev/İş) desenin
        // aynısı: liste içinde numarayı ayırt etmenin tek yolu.
        public string Etiket { get; set; } = string.Empty;

        // ⚠️ BUGÜN HİÇBİR YERDE TRUE YAPILMIYOR — SMS doğrulaması
        // Faz 2'nin işi. Alan şimdiden var çünkü sonradan eklemek
        // migration + geri doldurma kararı demekti ve o karar
        // ("eski numaralar doğrulanmış mı?") o gün de bugünkü
        // cevabı verecekti: hayır, bilmiyoruz.
        public bool DogrulandiMi { get; set; } = false;

        // Hesabın asıl numarası. Adres seçmeden yapılan işler
        // (kurtarma, bildirim) bunu kullanacak.
        //
        // ⚠️ "Tek varsayılan" kuralı KODDA korunuyor, veritabanında
        // değil. Filtreli unique index (WHERE VarsayilanMi = 1)
        // kurulabilirdi ama o zaman varsayılanı değiştirmek
        // "önce eskisini kapat, sonra yenisini aç" sırasına
        // mahkûm olurdu ve arada kalan an indeksi ihlal ederdi.
        public bool VarsayilanMi { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
