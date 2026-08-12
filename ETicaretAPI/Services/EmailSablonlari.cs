using System.Globalization;
using System.Net;
using ETicaretAPI.Models;

namespace ETicaretAPI.Services
{
    // E-posta içeriği: konu ve gövde BİRLİKTE üretilir.
    //
    // Neden tek nesne? Konu ile gövde ayrılmaz bir çift. Ayrı ayrı
    // döndürseydik her çağıran yerde ikisini eşleştirmek gerekirdi ve
    // "sipariş alındı gövdesi + kargo konusu" gibi karışmalar mümkün olurdu.
    //
    // record: değişmez (immutable) küçük veri taşıyıcısı. Üretildikten
    // sonra değişmemesi gereken şeyler için class'tan daha uygun —
    // yanlışlıkla değiştirmek derleme hatası verir.
    public record EmailIcerik(string Konu, string GovdeHtml);

    // Sipariş e-postalarında listelenecek ürün satırı.
    //
    // Neden OrderItem'ı doğrudan kullanmıyoruz? OrderItem'da ürün ADI yok,
    // sadece ProductId var. Şablonun veritabanına gitmesi ise yanlış olurdu:
    // şablonun tek işi metin üretmek. Veriyi çağıran taraf hazırlar.
    public record EmailSiparisKalemi(string UrunAdi, int Adet, decimal BirimFiyat);


    // ============================================================
    //  E-POSTA ŞABLONLARI
    //
    //  NEDEN AYRI BİR SINIF?
    //  Şablonlar şu ana kadar AuthController'ın içinde satır arasında
    //  yazılıyordu. Dört sipariş maili daha eklenince o dosya okunmaz
    //  hale gelirdi ve "başlık rengini değiştir" gibi bir istek sekiz
    //  ayrı yere dokunmayı gerektirirdi.
    //
    //  Burada ortak HTML iskeleti TEK yerde duruyor; her şablon sadece
    //  kendi gövdesini üretiyor.
    //
    //  NEDEN static DEĞİL?
    //  Mağaza adı, telefon ve adres appsettings'ten geliyor. static bir
    //  sınıf IConfiguration alamaz; alabilmesi için ya global bir
    //  değişken (kötü) ya da her metoda parametre olarak taşımak (gürültü)
    //  gerekirdi. Servis olarak kaydedip enjekte etmek en temizi.
    // ============================================================
    public class EmailSablonlari
    {
        private readonly IConfiguration _config;

        public EmailSablonlari(IConfiguration config)
        {
            _config = config;
        }


        // ========== YARDIMCILAR ==========

        // ⚠️ GÜVENLİK — HTML KAÇIŞI
        //
        // Müşteri adı, sipariş notu ve iptal sebebi KULLANICI GİRDİSİDİR.
        // Doğrudan HTML'e gömersek müşteri şunu yazabilir:
        //
        //     <a href="http://sahte-banka.com">Ödemeni güncelle</a>
        //
        // ...ve bu bağlantı, BİZİM mağaza adımızla giden bir e-postada
        // görünür. Alıcı bize güvendiği için tıklar. Buna HTML enjeksiyonu
        // denir ve e-postada, web sayfasından daha tehlikelidir: alıcı
        // gönderenin bizim olduğunu bildiği için savunması düşüktür.
        //
        // HtmlEncode, "<" karakterini "&lt;" yapar — tarayıcı/istemci onu
        // etiket olarak değil, düz metin olarak gösterir.
        //
        // KURAL: Kullanıcıdan gelen HER metin, HTML'e girmeden önce
        // buradan geçer. İstisna yok.
        private static string Kacir(string? metin)
        {
            // ?? string.Empty'yi SONA aldık.
            //
            // Önceki hali (HtmlEncode(metin ?? "")) girdiyi güvenceye
            // alıyordu ama ÇIKTIYI değil: HtmlEncode'un imzası string?
            // döndürüyor, dolayısıyla derleyici "null dönebilir" diye
            // uyarıyordu.
            //
            // Sonda kontrol etmek ikisini birden kapatıyor: metin null
            // olsa da, HtmlEncode null dönse de sonuç boş dize.
            return WebUtility.HtmlEncode(metin) ?? string.Empty;
        }

        // Para birimi biçimlendirme.
        //
        // Kültürü AÇIKÇA belirtiyoruz. Belirtmezsek sunucunun işletim
        // sistemi kültürü kullanılır: senin makinende "1.234,50" çıkar,
        // yurt dışındaki bir sunucuda "1,234.50" çıkar. Aynı kod, farklı
        // çıktı — bulunması en zor hata türü.
        private static readonly CultureInfo Kultur = new CultureInfo("tr-TR");

        private static string Para(decimal tutar)
        {
            return tutar.ToString("N2", Kultur) + " ₺";
        }

        // Tarih biçimlendirme.
        //
        // Veritabanındaki tüm tarihler UTC. Müşteriye Türkiye saatiyle
        // göstermemiz lazım.
        //
        // Neden TimeZoneInfo değil de sabit +3?
        //   TimeZoneInfo.FindSystemTimeZoneById() işletim sistemine göre
        //   farklı kimlikler ister ("Turkey Standard Time" Windows'ta,
        //   "Europe/Istanbul" Linux'ta) ve bulamazsa istisna fırlatır.
        //   Bir e-posta şablonunun sunucu yapılandırması yüzünden
        //   patlaması kabul edilemez.
        //
        //   Türkiye 2016'dan beri kalıcı olarak UTC+3 ve yaz saati
        //   uygulaması YOK. Yani sabit ofset burada doğru sonuç veriyor.
        //
        // ⚠️ Yurt dışına satış yapılırsa bu varsayım bozulur. O gün
        //    gelirse alıcının saat dilimi Order'a kaydedilmeli.
        private static string Tarih(DateTime utcTarih)
        {
            var turkiyeSaati = utcTarih.AddHours(3);
            return turkiyeSaati.ToString("dd.MM.yyyy HH:mm", Kultur);
        }

        // Mağaza bilgileri — koda gömmüyoruz, kargo etiketiyle aynı kaynak.
        private string MagazaAdi => _config["Magaza:Ad"] ?? "Mağaza";
        private string MagazaTelefon => _config["Magaza:Telefon"] ?? "";


        // ========== ORTAK HTML İSKELETİ ==========

        // Her e-postanın dış kabuğu. Başlık, gövde, altbilgi.
        //
        // Tasarım değişikliği gerektiğinde SADECE burası düzenlenir —
        // altı şablonun hepsi otomatik olarak yeni görünümü alır.
        //
        // vurguRengi: her mail türünün kendi rengi var (sipariş yeşil,
        // iptal kırmızı). Renk TEK BAŞINA bilgi taşımıyor — başlık metni
        // zaten ne olduğunu söylüyor. Renk sadece destekleyici.
        private string Iskelet(string baslik, string vurguRengi, string govde)
        {
            // ⭐ DÜZELTME: telefon satırını HTML'in İÇİNDE hesaplamıyoruz.
            //
            // Önceki hali $@"..." bloğunun deliği içinde ikinci bir $"..."
            // dizesi başlatıyordu ve derleyici tırnakları çözemiyordu
            // (CS1073). Karmaşık ifadeyi dışarı almak hem derlenir hem
            // HTML'i okunur bırakır.
            var telefonSatiri = string.IsNullOrWhiteSpace(MagazaTelefon)
                ? string.Empty
                : $"<br/>Sorularınız için: {Kacir(MagazaTelefon)}";

            // Mağaza adının büyük harfli hali de dışarıda hesaplanıyor —
            // aynı sebep: delik ne kadar sade olursa o kadar iyi.
            //
            // ToUpper() DEĞİL ToUpperInvariant(): Türkçe kültürde "i"
            // harfinin büyüğü "İ"dir, İngilizce kültürde "I". Sunucunun
            // kültür ayarına göre farklı çıktı üretmesini istemiyoruz.
            var magazaBaslik = Kacir(MagazaAdi).ToUpperInvariant();

            return $@"
<div style=""margin:0;padding:24px 12px;background-color:#f4f5f7;font-family:Arial,Helvetica,sans-serif;"">

  <!-- Dış tablo: içeriği ortalar.
       Neden tablo? margin:0 auto ile ortalama Outlook'ta çalışmaz.
       align=""center"" ile tablo her istemcide ortalanır. -->
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
    <tr>
      <td align=""center"">

        <!-- İç tablo: asıl kart. 600px e-posta standardı. -->
        <table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" border=""0""
               style=""width:600px;max-width:100%;background-color:#ffffff;border-radius:10px;overflow:hidden;"">

          <!-- ÜST ŞERİT -->
          <tr>
            <td style=""background-color:{vurguRengi};padding:20px 28px;"">
              <div style=""color:#ffffff;font-size:13px;letter-spacing:1px;"">
                {magazaBaslik}
              </div>
              <div style=""color:#ffffff;font-size:21px;font-weight:bold;margin-top:4px;"">
                {baslik}
              </div>
            </td>
          </tr>

          <!-- GÖVDE -->
          <tr>
            <td style=""padding:28px;color:#2c3e50;font-size:15px;line-height:1.6;"">
              {govde}
            </td>
          </tr>

          <!-- ALTBİLGİ -->
          <tr>
            <td style=""background-color:#fafbfc;padding:18px 28px;border-top:1px solid #e6e9ec;
                       color:#8a949e;font-size:12px;line-height:1.5;"">
              Bu e-posta {Kacir(MagazaAdi)} tarafından otomatik olarak gönderilmiştir.
              {telefonSatiri}
              <br/>Bu mesajı yanıtlamayınız.
            </td>
          </tr>

        </table>
      </td>
    </tr>
  </table>
</div>";
        }

        // Sipariş bilgilerini gösteren ortak kutu.
        // Dört sipariş mailinin dördünde de aynı — tek yerde tutuyoruz.
        private string SiparisOzetKutusu(Order siparis)
        {
            return $@"
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""
       style=""background-color:#f7f9fa;border-radius:8px;margin:18px 0;"">
  <tr>
    <td style=""padding:14px 16px;font-size:14px;color:#2c3e50;"">
      <b>Sipariş No:</b> {Kacir(siparis.OrderNumber)}<br/>
      <b>Tarih:</b> {Tarih(siparis.CreatedAt)}<br/>
      <b>Tutar:</b> {Para(siparis.Total)}
    </td>
  </tr>
</table>";
        }

        // Ürün listesi tablosu.
        private string KalemTablosu(List<EmailSiparisKalemi> kalemler, Order siparis)
        {
            // StringBuilder yerine string birleştirme yeterli: kalem sayısı
            // tipik olarak 1-10 arası. StringBuilder yüzlerce parçada anlamlı.
            var satirlar = string.Empty;

            foreach (var k in kalemler)
            {
                satirlar += $@"
  <tr>
    <td style=""padding:9px 0;border-bottom:1px solid #eef1f3;font-size:14px;"">
      {Kacir(k.UrunAdi)}<br/>
      <span style=""color:#8a949e;font-size:12px;"">{k.Adet} adet × {Para(k.BirimFiyat)}</span>
    </td>
    <td style=""padding:9px 0;border-bottom:1px solid #eef1f3;font-size:14px;
               text-align:right;white-space:nowrap;"">
      {Para(k.Adet * k.BirimFiyat)}
    </td>
  </tr>";
            }

            // İndirim satırı sadece indirim varsa. "İndirim: 0,00 ₺"
            // yazmak gereksiz gürültü olurdu — mobil sepet ekranındaki
            // kararla aynı.
            var indirimSatiri = string.Empty;

            if (siparis.DiscountAmount > 0)
            {
                var kuponEki = string.IsNullOrEmpty(siparis.CouponCode)
                    ? string.Empty
                    : $" ({Kacir(siparis.CouponCode)})";

                indirimSatiri = $@"
  <tr>
    <td style=""padding:6px 0;font-size:14px;color:#6b7680;"">Ara toplam</td>
    <td style=""padding:6px 0;font-size:14px;text-align:right;"">{Para(siparis.SubTotal)}</td>
  </tr>
  <tr>
    <td style=""padding:6px 0;font-size:14px;color:#27ae60;"">İndirim{kuponEki}</td>
    <td style=""padding:6px 0;font-size:14px;text-align:right;color:#27ae60;"">
      −{Para(siparis.DiscountAmount)}
    </td>
  </tr>";
            }

            return $@"
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
  {satirlar}
  {indirimSatiri}
  <tr>
    <td style=""padding:12px 0 0 0;font-size:16px;font-weight:bold;"">Toplam</td>
    <td style=""padding:12px 0 0 0;font-size:16px;font-weight:bold;text-align:right;"">
      {Para(siparis.Total)}
    </td>
  </tr>
</table>";
        }

        // Teslimat adresi kutusu — dondurulmuş alanlardan okunuyor.
        private string AdresKutusu(Order siparis)
        {
            var not = string.Empty;

            if (!string.IsNullOrWhiteSpace(siparis.CustomerNote))
            {
                not = $@"
      <div style=""margin-top:10px;padding-top:10px;border-top:1px dashed #d6dbe0;"">
        <b>Notunuz:</b><br/>{Kacir(siparis.CustomerNote)}
      </div>";
            }

            return $@"
<div style=""background-color:#f7f9fa;border-radius:8px;padding:14px 16px;
            font-size:14px;line-height:1.55;margin:18px 0;"">
  <b>Teslimat Adresi</b><br/>
  {Kacir(siparis.ShippingFullName)}<br/>
  {Kacir(siparis.ShippingFullAddress)}<br/>
  {Kacir(siparis.ShippingCity)}<br/>
  Tel: {Kacir(siparis.ShippingPhone)}
  {not}
</div>";
        }


        // ========== ŞABLONLAR ==========

        // 1) SİPARİŞ ALINDI
        public EmailIcerik SiparisAlindi(Order siparis, List<EmailSiparisKalemi> kalemler)
        {
            var govde = $@"
<p style=""margin:0 0 12px 0;"">Merhaba {Kacir(siparis.ShippingFullName)},</p>

<p style=""margin:0 0 4px 0;"">
  Siparişiniz başarıyla alındı ve hazırlanmaya başlandı.
  Kargoya verildiğinde takip numarasını içeren yeni bir e-posta göndereceğiz.
</p>

{SiparisOzetKutusu(siparis)}

<p style=""margin:18px 0 8px 0;font-weight:bold;"">Sipariş İçeriği</p>
{KalemTablosu(kalemler, siparis)}

{AdresKutusu(siparis)}";

            // Konuda sipariş numarası var: müşteri gelen kutusunda arama
            // yaptığında numarayla bulabilsin. Konu satırı bir arama
            // anahtarıdır, sadece bir başlık değil.
            return new EmailIcerik(
                $"Siparişiniz alındı — {siparis.OrderNumber}",
                Iskelet("Siparişiniz Alındı", "#27ae60", govde));
        }

        // 2) KARGOYA VERİLDİ
        public EmailIcerik KargoyaVerildi(Order siparis)
        {
            var govde = $@"
<p style=""margin:0 0 12px 0;"">Merhaba {Kacir(siparis.ShippingFullName)},</p>

<p style=""margin:0 0 4px 0;"">Siparişiniz kargoya teslim edildi.</p>

<div style=""background-color:#f0f7fd;border-left:4px solid #2980b9;border-radius:6px;
            padding:14px 16px;margin:18px 0;"">
  <div style=""font-size:13px;color:#6b7680;"">Kargo Firması</div>
  <div style=""font-size:16px;font-weight:bold;margin-bottom:10px;"">
    {Kacir(siparis.ShippingCompany)}
  </div>

  <div style=""font-size:13px;color:#6b7680;"">Takip Numarası</div>

  <!-- Eşit genişlikli font: numara karakter karakter okunuyor,
       ""1"" ile ""l"" ayrımı normal fontta zor.
       Bağlantı VERMİYORUZ — kargo firmalarının sorgu URL'leri
       değişiyor ve kırıldığında haberimiz olmuyor. Numarayı
       kopyalanabilir düz metin olarak veriyoruz. -->
  <div style=""font-size:18px;font-weight:bold;font-family:'Courier New',monospace;
              letter-spacing:1px;"">
    {Kacir(siparis.TrackingNumber)}
  </div>
</div>

<p style=""margin:0 0 4px 0;font-size:14px;color:#6b7680;"">
  Bu numarayı kargo firmasının web sitesinden veya uygulamasından sorgulayabilirsiniz.
</p>

{SiparisOzetKutusu(siparis)}
{AdresKutusu(siparis)}";

            return new EmailIcerik(
                $"Siparişiniz kargoya verildi — {siparis.OrderNumber}",
                Iskelet("Kargoya Verildi", "#2980b9", govde));
        }

        // 3) TESLİM EDİLDİ
        public EmailIcerik TeslimEdildi(Order siparis)
        {
            var teslimSatiri = siparis.DeliveredAt.HasValue
                ? $@"<p style=""margin:0 0 4px 0;"">Teslim tarihi: <b>{Tarih(siparis.DeliveredAt.Value)}</b></p>"
                : string.Empty;

            var govde = $@"
<p style=""margin:0 0 12px 0;"">Merhaba {Kacir(siparis.ShippingFullName)},</p>

<p style=""margin:0 0 4px 0;"">Siparişiniz teslim edildi. Bizi tercih ettiğiniz için teşekkür ederiz.</p>

{teslimSatiri}

{SiparisOzetKutusu(siparis)}

<div style=""background-color:#fdf7e3;border-radius:8px;padding:14px 16px;margin:18px 0;
            font-size:14px;line-height:1.55;"">
  <b>Ürünleri beğendiniz mi?</b><br/>
  Uygulamadan sipariş detayına girerek ürünleri değerlendirebilirsiniz.
  Yorumlarınız diğer müşterilerin doğru seçim yapmasına yardımcı oluyor.
</div>";

            return new EmailIcerik(
                $"Siparişiniz teslim edildi — {siparis.OrderNumber}",
                Iskelet("Teslim Edildi", "#27ae60", govde));
        }

        // 4) İPTAL EDİLDİ
        public EmailIcerik SiparisIptalEdildi(Order siparis, string? sebep)
        {
            var sebepKutusu = string.IsNullOrWhiteSpace(sebep)
                ? string.Empty
                : $@"
<div style=""background-color:#f7f9fa;border-radius:8px;padding:14px 16px;margin:18px 0;
            font-size:14px;line-height:1.55;"">
  <b>İptal sebebi</b><br/>{Kacir(sebep)}
</div>";

            var govde = $@"
<p style=""margin:0 0 12px 0;"">Merhaba {Kacir(siparis.ShippingFullName)},</p>

<p style=""margin:0 0 4px 0;"">Siparişiniz iptal edilmiştir.</p>

{sebepKutusu}
{SiparisOzetKutusu(siparis)}

<div style=""background-color:#fdf0ee;border-left:4px solid #c0392b;border-radius:6px;
            padding:14px 16px;margin:18px 0;font-size:14px;line-height:1.55;"">
  <b>İade Bilgisi</b><br/>
  {Para(siparis.Total)} tutarındaki ödemeniz iade edilmek üzere işleme alınmıştır.
  Tutarın hesabınıza yansıması bankanıza bağlı olarak birkaç iş günü sürebilir.
</div>";

            return new EmailIcerik(
                $"Siparişiniz iptal edildi — {siparis.OrderNumber}",
                Iskelet("Sipariş İptal Edildi", "#c0392b", govde));
        }

        // 5) ADMİN BAŞVURUSU ONAYLANDI
        public EmailIcerik BasvuruOnaylandi(string adSoyad)
        {
            var panelUrl = _config["Uygulama:PanelUrl"] ?? "";

            var panelSatiri = string.IsNullOrWhiteSpace(panelUrl)
                ? string.Empty
                : $@"<p style=""margin:16px 0 0 0;"">
                       Yönetim paneline buradan giriş yapabilirsiniz:<br/>
                       <a href=""{panelUrl}"">{Kacir(panelUrl)}</a>
                     </p>";

            var govde = $@"
<p style=""margin:0 0 12px 0;"">Merhaba {Kacir(adSoyad)},</p>

<p style=""margin:0 0 4px 0;"">
  Yöneticilik başvurunuz onaylandı. Hesabınız artık yönetici yetkilerine sahip.
</p>

<p style=""margin:12px 0 0 0;"">
  <b>Güvenlik nedeniyle mevcut oturumlarınız sonlandırıldı.</b>
  Yeni yetkilerinizin geçerli olması için tekrar giriş yapmanız gerekiyor.
</p>

{panelSatiri}";

            // Yeşil: olumlu karar. Renk tek başına bilgi taşımıyor,
            // başlık zaten ne olduğunu söylüyor.
            return new EmailIcerik(
                "Yöneticilik başvurunuz onaylandı",
                Iskelet("Başvurunuz Onaylandı", "#27ae60", govde));
        }


        // 6) ADMİN BAŞVURUSU REDDEDİLDİ
        public EmailIcerik BasvuruReddedildi(string adSoyad, string? redNedeni)
        {


            // ⭐ YENİ — bekleme süresi metni AYARDAN besleniyor.
            //
            // ⚠️ Buraya "30 gün" diye yazsaydık, ayarı 3 saate
            // çekmemize rağmen mail hâlâ 30 gün derdi. Kimse fark
            // etmezdi çünkü hiçbir yerde hata vermezdi — sadece
            // müşteriye yanlış bilgi giderdi.
            //
            // "İki yerde yazılan gerçek er ya da geç ikiye ayrılır."
            // AuthController ile bu şablon AYNI ayardan okuyor.
            var beklemeSaati = _config.GetValue<int?>("Basvuru:RedSonrasiBeklemeSaati") ?? 3;

            // Bekleme kapalıysa (0) "0 saat sonra başvurun" demek
            // saçma olurdu — cümleyi tamamen değiştiriyoruz.
            string beklemeMetni;

            if (beklemeSaati <= 0)
            {
                beklemeMetni = "Dilerseniz yeniden başvurabilirsiniz.";
            }
            else if (beklemeSaati < 24)
            {
                beklemeMetni = $"Dilerseniz {beklemeSaati} saat sonra yeniden başvurabilirsiniz.";
            }
            else
            {
                // 24'ün katı değilse aşağı yuvarlıyoruz — "1,5 gün"
                // gibi bir ifade e-postada tuhaf durur. Kullanıcı
                // erken denerse zaten aynı cevabı alır, geç denerse
                // sorun yok.
                var gun = beklemeSaati / 24;
                beklemeMetni = $"Dilerseniz {gun} gün sonra yeniden başvurabilirsiniz.";
            }

            var tekrarSatiri = $@"<p style=""margin:16px 0 0 0;"">{Kacir(beklemeMetni)}</p>";


            // ⚠️ redNedeni SÜPERADMİNİN YAZDIĞI SERBEST METİN —
            // yani kullanıcı girdisi. HTML'e girmeden önce Kacir()
            // ile kaçırılmak zorunda. İstisna yok.
            var nedenKutusu = string.IsNullOrWhiteSpace(redNedeni)
                ? string.Empty
                : $@"<div style=""margin:16px 0 0 0;padding:14px 16px;background-color:#fafbfc;
                            border-left:3px solid #e74c3c;border-radius:4px;"">
                       <b>Gerekçe</b><br/>
                       {Kacir(redNedeni)}
                     </div>";

            var govde = $@"
<p style=""margin:0 0 12px 0;"">Merhaba {Kacir(adSoyad)},</p>

<p style=""margin:0 0 4px 0;"">
  Yöneticilik başvurunuz bu kez olumlu sonuçlanmadı.
  Mevcut hesabınız ve alışveriş geçmişiniz etkilenmedi, normal şekilde
  kullanmaya devam edebilirsiniz.
</p>

{nedenKutusu}

{tekrarSatiri}";

            // ⚠️ Kırmızı DEĞİL turuncu.
            // Kırmızıyı gerçekten geri alınamaz/kritik olaylara
            // saklıyoruz (sipariş iptali gibi). Bir başvurunun
            // reddi kötü haber ama felaket değil.
            return new EmailIcerik(
                "Yöneticilik başvurunuz hakkında",
                Iskelet("Başvuru Sonucu", "#e67e22", govde));
        }


        // 8) ⭐ YENİ (Aşama 9) — İADE TALEBİ DURUM BİLDİRİMİ
        //
        // ⚠️ TEK ŞABLON, ÜÇ DURUM. Onay, red ve para iadesi için ayrı
        // üç şablon yazmak cazipti; elendi. Üçünün iskeleti birebir
        // aynı (selam + sipariş özeti + duruma göre bir kutu) ve
        // ayrı yazsaydık yarın imza satırı değiştiğinde üçünü birden
        // güncellemek gerekirdi — biri unutulurdu.
        //
        // ⚠️ Metot adı `IadeDurumBildirimi`, `IadeDurumu` DEĞİL:
        // `IadeDurumu` bir sabit sınıfının adı ve aynı isim, gövde
        // içinde `IadeDurumu.Reddedildi` yazmayı imkânsız kılardı.
        public EmailIcerik IadeDurumBildirimi(Order siparis, ReturnRequest talep)
        {
            string baslik;
            string renk;
            string kutu;

            if (talep.Durum == IadeDurumu.Reddedildi)
            {
                baslik = "İade Talebiniz Reddedildi";
                renk = "#c0392b";

                // ⚠️ Red nedeni HER ZAMAN yazılıyor (uç onu zorunlu
                // tutuyor). "Reddedildi" tek başına bir cevap değil.
                kutu = $@"
<div style=""background-color:#fdf0ee;border-left:4px solid #c0392b;border-radius:6px;
            padding:14px 16px;margin:18px 0;font-size:14px;line-height:1.55;"">
  <b>Red nedeni</b><br/>{Kacir(talep.RedNedeni)}
</div>";
            }
            else if (talep.Durum == IadeDurumu.ParaIadeEdildi)
            {
                baslik = "İade Tutarınız Ödendi";
                renk = "#27ae60";

                kutu = $@"
<div style=""background-color:#eef8f1;border-left:4px solid #27ae60;border-radius:6px;
            padding:14px 16px;margin:18px 0;font-size:14px;line-height:1.55;"">
  <b>İade Bilgisi</b><br/>
  {Para(talep.IadeTutari ?? 0)} tutarındaki iadeniz işleme alınmıştır.
  Tutarın hesabınıza yansıması bankanıza bağlı olarak birkaç iş günü sürebilir.
</div>";
            }
            else
            {
                baslik = "İade Talebiniz Onaylandı";
                renk = "#e67e22";

                // ⚠️ Onay maili müşteriye NE YAPACAĞINI söylüyor.
                // Yalnızca "onaylandı" yazsaydık müşteri bekler,
                // biz paketi bekler, iki taraf da karşıdakini
                // beklerdi.
                kutu = $@"
<div style=""background-color:#fdf6ec;border-left:4px solid #e67e22;border-radius:6px;
            padding:14px 16px;margin:18px 0;font-size:14px;line-height:1.55;"">
  <b>Sırada ne var?</b><br/>
  Ürünü orijinal paketiyle kargoya verebilirsiniz. Paket bize ulaştığında
  kontrol edilecek ve ardından ödemeniz iade edilecektir.
</div>";
            }

            var govde = $@"
<p style=""margin:0 0 12px 0;"">Merhaba {Kacir(siparis.ShippingFullName)},</p>

<p style=""margin:0 0 4px 0;"">
  {Kacir(siparis.OrderNumber)} numaralı siparişinizle ilgili iade talebiniz
  hakkında bir gelişme var.
</p>

{kutu}
{SiparisOzetKutusu(siparis)}";

            return new EmailIcerik(
                $"İade talebiniz — {siparis.OrderNumber}",
                Iskelet(baslik, renk, govde));
        }


        // 7) ⭐ YENİ (5.5) — BEKLENEN ÜRÜN STOĞA GELDİ
        public EmailIcerik StokaGeldi(string adSoyad, string urunAdi, int urunId)
        {
            // Mağaza sitesi tanımlıysa ürüne doğrudan link veriyoruz.
            //
            // ⚠️ Tanımlı değilse satır HİÇ çizilmiyor — boş bir href
            // ya da "#" veren bir bağlantı, tıklayan müşteriyi hiçbir
            // yere götürmez ve maili bozuk gösterirdi.
            var siteUrl = _config["Uygulama:SiteUrl"] ?? "";

            var urunSatiri = string.IsNullOrWhiteSpace(siteUrl)
                ? string.Empty
                : $@"<p style=""margin:16px 0 0 0;"">
                       <a href=""{siteUrl}/urun/{urunId}"">Ürüne git</a>
                     </p>";

            // ⚠️ urunAdi veritabanından geliyor ama yine de Kacir()
            // ile kaçırılıyor. Ürün adını panelden bir yönetici
            // yazıyor; "veritabanından geldi" güvenli demek değil.
            var govde = $@"
<p style=""margin:0 0 12px 0;"">Merhaba {Kacir(adSoyad)},</p>

<p style=""margin:0 0 4px 0;"">
  Beklediğiniz <b>{Kacir(urunAdi)}</b> yeniden stoklarımızda.
</p>

<p style=""margin:12px 0 0 0;"">
  Stok sınırlı olabilir; sipariş vermek isterseniz gecikmemenizi öneririz.
</p>

{urunSatiri}

<p style=""margin:20px 0 0 0;font-size:13px;color:#7f8c8d;"">
  Bu bildirimi, ürün tükendiğinde ""stoka gelince haber ver"" dediğiniz
  için aldınız. Tek seferliktir; tekrar haber almak isterseniz ürün
  sayfasından yeniden talep edebilirsiniz.
</p>";

            // Yeşil: beklenen, olumlu haber.
            return new EmailIcerik(
                $"{urunAdi} yeniden stokta",
                Iskelet("Ürün Stoğa Geldi", "#27ae60", govde));
        }
    }
}