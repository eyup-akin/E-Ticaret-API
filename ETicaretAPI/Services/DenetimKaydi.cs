using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.Support;

namespace ETicaretAPI.Services
{
    // ⭐ YENİ — DENETİM KAYDI YAZICISI, TEK YERDE
    //
    // ⚠️ NEDEN AYRI BİR SERVİS?
    //
    // Kayıt yazma kodu ÜÇ yerde ayrı ayrı duruyordu:
    //   • AdminController.LogEkle  → private, başkası kullanamıyordu
    //   • ReviewsController        → AuditLogs.Add(...) elle
    //   • SozlesmelerController    → AuditLogs.Add(...) elle
    //
    // Private olması asıl sorundu: denetim kaydına en çok ihtiyaç duyan
    // iki işlem (para iadesi ve admin sipariş iptali) başka
    // controller'larda yaşıyor ve o metoda erişemedikleri için HİÇ kayıt
    // tutmuyorlardı. Yani sistemde gerçek para hareketi yaratan iki
    // işlemin "kim yaptı" cevabı yoktu.
    //
    // StokDefteri ile aynı gerekçe ve aynı şekil: tek nokta olunca
    // unutmak imkânsız hale geliyor.
    //
    // ⚠️ SaveChanges ÇAĞIRMIYOR — bilerek, StokDefteri'ndeki desenin
    // aynısı. Kayıt, tetikleyen işlemle AYNI transaction'da yazılmalı.
    // Kendi başına kaydetseydi, işlem geri alındığında defterde
    // "yapıldı" yazan hayalet bir kayıt kalırdı.
    //
    // ⚠️⚠️ FIRE-AND-FORGET (beklemeden yazma) BİLİNÇLİ OLARAK REDDEDİLDİ.
    // İstek bitince scoped DbContext dispose ediliyor; arka planda devam
    // eden kod ObjectDisposedException alır — yük altında çıkan, tekrar
    // üretilmesi çok zor bir hata. Ayrıca işlem geri alındığı hâlde
    // "fiyat değişti" diyen bir kayıt YALAN SÖYLER.
    public class DenetimKaydi
    {
        private readonly AppDbContext _context;

        // ⭐ YENİ — isteğin geldiği adresi okumak için.
        //
        // ⚠️ IHttpContextAccessor, HttpContext DEĞİL: bu servis Hangfire
        // işlerinden de çağrılabilir ve orada ortada istek yok. Accessor
        // o durumda null döndürüyor, IP de null kalıyor.
        private readonly IHttpContextAccessor _istekErisimi;

        public DenetimKaydi(AppDbContext context, IHttpContextAccessor istekErisimi)
        {
            _context = context;
            _istekErisimi = istekErisimi;
        }

        /// <summary>
        /// Denetim kaydı ekler (context'e EKLER, kaydetmez).
        /// </summary>
        /// <param name="yapanId">İşlemi yapan (token'dan okunmuş) kullanıcı.</param>
        /// <param name="hedefId">İşlemden etkilenen kayıt/kullanıcı.</param>
        /// <param name="hedefAd">Etkilenen kaydın okunur adı — DONUYOR.</param>
        public async Task EkleAsync(
            int yapanId,
            int hedefId,
            string hedefAd,
            string islem,
            string? eski = null,
            string? yeni = null)
        {
            // ⚠️ Yapanın adı BURADA okunup kayda KOPYALANIYOR.
            //
            // Users tablosuna JOIN ile bağlamak daha "temiz" görünürdü
            // ama yanlış olurdu: admin hesabı yarın kapatılırsa
            // anonimleştirme sonrası bütün geçmiş kayıtlar
            // "Silinmiş Kullanıcı" yapan tarafından yapılmış görünürdü.
            // Denetim kaydı bir DELİLDİR; o günkü hali dondurulur.
            // (Sipariş adresinin dondurulmasıyla aynı ilke.)
            var yapanAd = await _context.Users
                .Where(u => u.Id == yapanId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync() ?? "Bilinmeyen";

            _context.AuditLogs.Add(new AuditLog
            {
                ActorUserId = yapanId,
                ActorName = yapanAd,
                TargetUserId = hedefId,
                TargetName = hedefAd,
                Action = islem,
                OldValue = eski,
                NewValue = yeni,
                IpAdresi = IstemciAdresi.Oku(_istekErisimi.HttpContext),
                CreatedAt = DateTime.UtcNow
            });
        }
    }


    // ⭐ YENİ — DENETİM İŞLEM KODLARI
    //
    // ⚠️ Kodlar elle yazılı metinlerdi ve dört dosyaya dağılmıştı.
    // Yazım hatası SESSİZ bir hataydı: "yorum_gizlendı" yazsan hiçbir
    // şey patlamaz, kayıt yazılır, ama denetim ekranındaki filtre onu
    // asla bulamaz.
    //
    // Sabit sınıfa toplanınca yanlış yazmak derleme hatası veriyor.
    // SiparisDurumlari ve IadeDurumu ile aynı desen.
    //
    // ⚠️ Bu kodlar admin panelindeki ISLEM_BILGI sözlüğüyle eşleşmeli
    // (DenetimSekmesi.jsx). Eşleşmezse ekran çökmez — ham kodu
    // gösterir — ama okunaksız olur.
    public static class DenetimIslemi
    {
        public const string RolDegisti = "rol_degisti";
        public const string BasvuruOnaylandi = "basvuru_onaylandi";
        public const string BasvuruReddedildi = "basvuru_reddedildi";
        public const string Aktiflestirildi = "aktiflestirildi";
        public const string Pasiflestirildi = "pasiflestirildi";
        public const string YorumGizlendi = "yorum_gizlendi";
        public const string YorumGosterildi = "yorum_gosterildi";
        public const string SozlesmeGuncellendi = "sozlesme_guncellendi";

        // ⭐ YENİ — para hareketi yaratan iki işlem.
        // İkisi de bugüne kadar hiç kayda geçmiyordu.
        public const string ParaIadesi = "para_iadesi";
        public const string SiparisIptalAdmin = "siparis_iptal_admin";

        // ⭐⭐ YENİ — PARAYA VE ENVANTERE DOKUNAN İŞLEMLER.
        //
        // ⚠️ Bunların hiçbiri bugüne kadar kaydedilmiyordu: fiyat
        // değiştiren, stok düzelten ve indirim uygulayan uçlar denetim
        // dışıydı. Asıl boşluk buradaydı.
        public const string UrunEklendi = "urun_eklendi";
        public const string UrunGuncellendi = "urun_guncellendi";
        public const string UrunSilindi = "urun_silindi";
        public const string UrunArsivlendi = "urun_arsivlendi";
        public const string UrunArsivdenCikarildi = "urun_arsivden_cikarildi";
        public const string StokDuzeltildi = "stok_duzeltildi";

        public const string IndirimUygulandi = "indirim_uygulandi";
        public const string IndirimKaldirildi = "indirim_kaldirildi";

        public const string KuponOlusturuldu = "kupon_olusturuldu";
        public const string KuponGuncellendi = "kupon_guncellendi";
        public const string KuponSilindi = "kupon_silindi";

        public const string KampanyaEklendi = "kampanya_eklendi";
        public const string KampanyaGuncellendi = "kampanya_guncellendi";
        public const string KampanyaSilindi = "kampanya_silindi";

        public const string KategoriEklendi = "kategori_eklendi";
        public const string KategoriGuncellendi = "kategori_guncellendi";
        public const string KategoriSilindi = "kategori_silindi";

        public const string IceAktarmaBaslatildi = "ice_aktarma_baslatildi";
    }


    // ⭐ YENİ — DENETİM KAYDINDA HEDEFİN OKUNUR ADI
    //
    // ⚠️ Id her zaman yazılıyor: ad değişebilir ya da iki kayıt aynı adı
    // taşıyabilir; adsız bir kayıt "hangi ürün" sorusunu cevapsız bırakır.
    //
    // ⚠️ Bu etiketler `AuditLog.TargetName` alanına gidiyor ve
    // `TargetUserId` bu durumlarda İŞLEMİ YAPAN adminin kimliğini
    // taşıyor: ürünün/kuponun "etkilenen kullanıcısı" yok. Sözleşme
    // güncellemesinde verilen kararın aynısı — böylece denetim
    // ekranındaki kişi bağlantısı geçerli bir kullanıcıya gidiyor.
    //
    // ⚠️⚠️ HEPSİ "Tür: değer" BİÇİMİNDE VE İKİ NOKTA ZORUNLU.
    // Hesap kapatma akışı, kişi adı ile varlık etiketini tam olarak bu
    // iki noktaya bakarak ayırıyor (AuthController.HesabimiSil). İki
    // noktasız bir etiket yazılırsa kişi adı sanılır ve maskelenir —
    // yani "Ürün X" etiketi sessizce "E***" olur.
    // Yeni bir etiket türü BURAYA eklenir, çağrı yerinde elle yazılmaz.
    public static class DenetimEtiketi
    {
        public static string Urun(int id, string ad) => $"Ürün: {ad} (#{id})";
        public static string Siparis(string siparisNo) => $"Sipariş: {siparisNo}";
        public static string Kupon(int id, string kod) => $"Kupon: {kod} (#{id})";
        public static string Kategori(int id, string ad) => $"Kategori: {ad} (#{id})";
        public static string Kampanya(int id, string baslik) => $"Kampanya: {baslik} (#{id})";
        public static string IceAktarma(int id, string dosya) => $"İçe aktarma: {dosya} (#{id})";
    }


    // ⭐ YENİ — DENETİM DEĞERİ YAZICISI (OldValue / NewValue)
    //
    // ⚠️⚠️ VARLIK ASLA SERIALIZE EDİLMEZ.
    //
    // JsonSerializer.Serialize(urun) yazmak cazip gelir ve o anda log'a
    // PasswordHash, SecurityStamp, token ya da kart bilgisi düşer.
    // Denetim kayıtlarında sırların sızdığı bir numaralı yol budur.
    //
    // Bu yüzden buradaki tek giriş noktası bir SÖZLÜK alıyor: yazılacak
    // alanları çağıran ELLE seçmek zorunda. Beyaz liste, kara liste
    // değil — yeni bir alan eklendiğinde varsayılan davranış "yazma".
    public static class DenetimDegeri
    {
        /// <summary>
        /// Elle kurulmuş alan sözlüğünü JSON metnine çevirir.
        /// Sözlük boşsa null döner (boş "{}" yazmanın bilgisi yok).
        /// </summary>
        public static string? Yaz(IDictionary<string, object?> alanlar)
        {
            if (alanlar.Count == 0)
            {
                return null;
            }

            // ⚠️ Türkçe karakterler kaçış dizisine dönüşmesin diye
            // Encoder gevşetiliyor; kayıt ekranda okunacak.
            return System.Text.Json.JsonSerializer.Serialize(alanlar,
                new System.Text.Json.JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder
                        .UnsafeRelaxedJsonEscaping
                });
        }

        /// <summary>
        /// Tek alanlık kısayol — "fiyat: 100 → 80" gibi durumlar için.
        /// </summary>
        public static string? Yaz(string ad, object? deger)
        {
            return Yaz(new Dictionary<string, object?> { [ad] = deger });
        }

        /// <summary>
        /// İki sözlüğü karşılaştırıp YALNIZCA farklı olan anahtarları
        /// döndürür.
        /// </summary>
        //
        // ⚠️ Değişmeyen alanları da yazsaydık her "Kaydet" tıklaması
        // dolu bir kayıt üretirdi ve gerçek fiyat değişikliği o
        // gürültünün içinde kaybolurdu.
        public static (Dictionary<string, object?> Eski, Dictionary<string, object?> Yeni)
            Degisenler(
                Dictionary<string, object?> onceki,
                Dictionary<string, object?> sonraki)
        {
            var eski = new Dictionary<string, object?>();
            var yeni = new Dictionary<string, object?>();

            foreach (var anahtar in onceki.Keys)
            {
                // ⚠️ Equals, == DEĞİL: object üzerinde == referans
                // karşılaştırması yapar ve kutulanmış (boxed) decimal
                // değerlerde her zaman "değişti" derdi — sessiz yanlış.
                if (!Equals(onceki[anahtar], sonraki.GetValueOrDefault(anahtar)))
                {
                    eski[anahtar] = onceki[anahtar];
                    yeni[anahtar] = sonraki.GetValueOrDefault(anahtar);
                }
            }

            return (eski, yeni);
        }
    }
}
