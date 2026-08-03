namespace ETicaretAPI.Services
{
    // IEmailGonderici için güvenli gönderim yardımcısı.
    //
    // ============================================================
    //  NEDEN VAR? — 1.6'NIN EN KRİTİK KURALI
    //
    //  Mail gönderimi ASLA asıl işlemi bozmamalı.
    //
    //  Örnek: müşteri sipariş verdi, stok düştü, ödeme alındı,
    //  transaction commit oldu. Sonra mail gönderilirken SMTP sunucusu
    //  cevap vermedi ve istisna fırlattı. Bu istisna yakalanmazsa
    //  global hata middleware'i devreye girer ve müşteri "500 Sunucu
    //  Hatası" görür.
    //
    //  Ama sipariş OLUŞTU. Müşteri hata gördüğü için tekrar dener ve
    //  ikinci bir sipariş verir. Stok iki kez düşer, kart iki kez
    //  çekilir. Bir bildirim maili yüzünden.
    //
    //  Bu dersi hesap kapatmada zaten öğrendik — burada da geçerli.
    //
    //  NEDEN HER ÇAĞRIYA try/catch YAZMIYORUZ?
    //  Dört tetikleme noktası var ve her birinde 6 satırlık aynı
    //  try/catch bloğunu tekrarlamak gerekirdi. Bir tanesinde unutmak
    //  yeterli — ve unutulan yer, en kötü anda ortaya çıkar.
    //  Tek satırlık bir çağrı, unutulması imkânsız bir kural yaratıyor.
    //
    //  NEDEN UZANTI METODU (extension method)?
    //  Çağrı yerinde doğal okunuyor:
    //      await _email.GuvenliGonderAsync(...)
    //  Ayrı bir yardımcı sınıf olsaydı çağıranın onu da enjekte etmesi
    //  gerekirdi. Uzantı metodu, arayüzü değiştirmeden yeni davranış
    //  ekliyor — IEmailGonderici sözleşmesi sade kalıyor.
    // ============================================================
    public static class EmailGondericiUzantilari
    {
        public static async Task GuvenliGonderAsync(
            this IEmailGonderici gonderici,
            ILogger logger,
            string aliciEmail,
            EmailIcerik icerik,
            string olayAdi)
        {
            // Alıcı adresi yoksa denemeye bile gerek yok.
            // (Hesabı kapatılmış kullanıcıların e-postası maskeleniyor.)
            if (string.IsNullOrWhiteSpace(aliciEmail))
            {
                logger.LogWarning(
                    "E-posta atlandı — alıcı adresi boş. Olay: {Olay}", olayAdi);
                return;
            }

            try
            {
                await gonderici.GonderAsync(aliciEmail, icerik.Konu, icerik.GovdeHtml);
            }
            catch (Exception hata)
            {
                // ⚠️ İSTİSNAYI YUTUYORUZ — bilinçli bir karar.
                //
                // Normalde istisna yutmak kötü bir uygulamadır çünkü hata
                // sessizce kaybolur. Burada iki sebeple doğru:
                //
                //   1) Yapılacak bir şey yok. Kullanıcıya "mailin
                //      gitmedi" demenin faydası yok, sipariş oluştu.
                //   2) Sessiz değil — LogError ile kaydediyoruz.
                //      "Yutmak" ile "loglayıp devam etmek" farklı şeyler.
                //
                // olayAdi parametresi burada işe yarıyor: log'da hangi
                // bildirimin başarısız olduğu net görünüyor.
                logger.LogError(hata,
                    "E-posta gönderilemedi. Olay: {Olay}, Alıcı: {Alici}",
                    olayAdi, aliciEmail);
            }
        }
    }
}