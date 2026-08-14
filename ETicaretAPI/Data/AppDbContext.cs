using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Models;

namespace ETicaretAPI.Data
{
    public class AppDbContext : DbContext
    {
        // Constructor: bağlantı ayarlarını dışarıdan alır (Program.cs'te vereceğiz)
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // Her DbSet = bir veritabanı tablosu
        public DbSet<User> Users { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<Card> Cards { get; set; }
        public DbSet<CartItem> CartItems { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<ProductImage> ProductImages { get; set; }
        public DbSet<Favorite> Favorites { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<Review> Reviews { get; set; }

        // ⭐ YENİ — stok hareket defteri
        public DbSet<StockMovement> StockMovements { get; set; }

        // ⭐ YENİ (5.5) — "stoka gelince haber ver" istekleri
        public DbSet<StockAlert> StockAlerts { get; set; }
        public DbSet<ImportJob> ImportJobs { get; set; } // ⭐ YENİ

        public DbSet<RefreshToken> RefreshTokens { get; set; } // ⭐ YENİ

        public DbSet<Coupon> Coupons { get; set; }
        public DbSet<CouponUsage> CouponUsages { get; set; }

        // ⭐ YENİ — admin olma başvuruları
        public DbSet<AdminBasvuru> AdminBasvurular { get; set; }

        // ⭐ YENİ (4.9) — müşterinin telefon defteri
        public DbSet<Phone> Phones { get; set; }

        // ⭐ YENİ (Aşama 8) — destek talepleri ve yazışmaları
        public DbSet<SupportTicket> SupportTickets { get; set; }
        public DbSet<SupportMessage> SupportMessages { get; set; }

        // ⭐ YENİ (Aşama 9) — iade talepleri
        public DbSet<ReturnRequest> ReturnRequests { get; set; }

        // ⭐ YENİ (Aşama 10) — sözleşmeler ve onay kayıtları
        public DbSet<Sozlesme> Sozlesmeler { get; set; }
        public DbSet<SozlesmeOnayi> SozlesmeOnaylari { get; set; }

        // ⭐ YENİ — ürün kombinleri
        public DbSet<Kombin> Kombinler { get; set; }
        public DbSet<KombinUrun> KombinUrunler { get; set; }

        // ⭐ YENİ — müşterinin kaydettiği siparişler ("hızlı siparişler")
        public DbSet<HizliSiparis> HizliSiparisler { get; set; }

        // ⭐ YENİ (B2) — ana sayfa afişleri / kampanyalar
        public DbSet<Kampanya> Kampanyalar { get; set; }

        // ⭐ YENİ — SİSTEM KAYITLARI (log tabloları).
        // AuditLogs da bu ailenin üyesi; o zaten yukarıda tanımlı.
        public DbSet<EmailKaydi> EmailKayitlari { get; set; }
        public DbSet<GirisKaydi> GirisKayitlari { get; set; }
        public DbSet<HataKaydi> HataKayitlari { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Bir kullanıcı bir ürüne YALNIZCA BİR yorum yapabilsin
            modelBuilder.Entity<Review>()
                .HasIndex(r => new { r.UserId, r.ProductId })
                .IsUnique();

            // Ürün silinince yorumları da silinsin
            modelBuilder.Entity<Review>()
                .HasOne<Product>()
                .WithMany()
                .HasForeignKey(r => r.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // Kullanıcı silinmiyor (soft delete) → yoruma dokunma, engelle
            modelBuilder.Entity<Review>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // DECIMAL PRECISION — para alanları 18 basamak / 2 kuruş
            modelBuilder.Entity<Product>()
                .Property(p => p.Price)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Order>()
                .Property(o => o.Total)
                .HasPrecision(18, 2);

            modelBuilder.Entity<OrderItem>()
                .Property(oi => oi.UnitPrice)
                .HasPrecision(18, 2);

            // ⭐ YENİ — dondurulmuş maliyet de bir para alanı.
            //
            // Neden precision belirtmek zorundayız?
            // SQL Server'da decimal'in varsayılanı decimal(18,0) —
            // yani KURUŞ YOK. 12,50 TL veritabanına 13 olarak yazılırdı
            // ve bunu hiçbir hata mesajı söylemezdi.
            // Diğer tüm para alanlarında (Price, Total, UnitPrice, Cost)
            // aynı ayarı yaptık; bu onların devamı.
            modelBuilder.Entity<OrderItem>()
                .Property(oi => oi.UnitCost)
                .HasPrecision(18, 2);

            // ⭐ YENİ — dondurulmuş ürün adına uzunluk sınırı.
            //
            // Sınır koymazsak EF bu kolonu nvarchar(max) yapar.
            // nvarchar(max) SQL Server'da "büyük nesne" sayılır: satırın
            // dışında ayrı sayfalarda saklanabilir, index kurulamaz ve
            // her okumada ek maliyet getirir. Ürün adı için gereksiz.
            //
            // 200 seçildi çünkü gerçekçi bir üst sınır. Backfill
            // sırasında LEFT(Name, 200) kullanacağız — böylece daha uzun
            // bir ad varsa migration hata vermek yerine kırpar.
            modelBuilder.Entity<OrderItem>()
                .Property(oi => oi.ProductName)
                .HasMaxLength(200);

            modelBuilder.Entity<Payment>()
                .Property(p => p.Amount)
                .HasPrecision(18, 2);

            // ⭐ YENİ — maliyet de para alanı, aynı hassasiyet
            modelBuilder.Entity<Product>()
                .Property(p => p.Cost)
                .HasPrecision(18, 2);

            // ⭐ YENİ (B1) — indirim öncesi fiyat da bir para alanı.
            //
            // ⚠️ Bunlar olmadan EF uyarı veriyordu: "No store type was
            // specified for the decimal property 'EskiFiyat'". Varsayılan
            // yine decimal(18,2) üretiyor ama AÇIKÇA yazmak şart —
            // diğer bütün para alanları burada yapılandırılmış durumda
            // ve bu ikisi listeye girmezse, yarın varsayılan değişirse
            // sessizce sapacaklar.
            modelBuilder.Entity<Product>()
                .Property(p => p.EskiFiyat)
                .HasPrecision(18, 2);

            modelBuilder.Entity<OrderItem>()
                .Property(oi => oi.EskiFiyat)
                .HasPrecision(18, 2);

            // ⚠️ Precision şart: varsayılan decimal(18,0) kuruşu siler.
            modelBuilder.Entity<Order>()
                .Property(o => o.KombinIndirimi)
                .HasPrecision(18, 2);


            // ⭐ YENİ — sipariş numarası benzersiz olmalı.
            // Bu index performans için DEĞİL, DOĞRULUK için:
            // aynı numara iki siparişe verilemesin diye son savunma hattı.
            // Kodda kaç kontrol yaparsak yapalım, garantiyi veritabanı verir.
            modelBuilder.Entity<Order>()
                .HasIndex(o => o.OrderNumber)
                .IsUnique();


            // ⭐ YENİ — ÇİFT SİPARİŞ KORUMASI
            //
            // Bu index performans için DEĞİL, DOĞRULUK için.
            // Kodda "bu anahtarla sipariş var mı?" diye kontrol
            // ediyoruz ama iki istek aynı anda gelirse ikisi de
            // "yok" cevabı alabilir. Kontrol ile yazma arasındaki
            // o mikrosaniyeyi kapatan tek şey bu index.
            //
            // NEDEN BİLEŞİK (UserId + Anahtar)?
            // Anahtarı İSTEMCİ üretiyor, biz değil. Sadece anahtara
            // unique index koysaydık iki farklı kullanıcının anahtarı
            // çakıştığında ikincisinin siparişi reddedilirdi.
            // Kullanıcıyı index'e katınca çakışma imkânsız hale gelir.
            //
            // NEDEN FİLTRE?
            // SQL Server'da unique index NULL'ları "birbirine eşit"
            // sayar — filtresiz olsaydı anahtarsız İKİNCİ sipariş
            // hata verirdi. Filtre sayesinde kural sadece anahtarı
            // DOLU satırlar için işliyor. (Product.Barcode'da EF bunu
            // otomatik yapmıştı; burada bileşik index olduğu için
            // açıkça yazıyoruz — davranış tahmine bırakılmasın.)
            modelBuilder.Entity<Order>()
                .HasIndex(o => new { o.UserId, o.IdempotencyKey })
                .IsUnique()
                .HasFilter("[IdempotencyKey] IS NOT NULL");

            // nvarchar(max) kolonuna index kurulamaz — uzunluk şart.
            // 64 karakter, ürettiğimiz anahtarın iki katından fazla.
            modelBuilder.Entity<Order>()
                .Property(o => o.IdempotencyKey)
                .HasMaxLength(64);


            // ⭐ YENİ — ürün açıklaması uzunluk sınırı.
            //
            // Belirtmeseydik EF bu kolonu nvarchar(max) yapardı.
            // İki sorunu var:
            //   1) Sınırsız metin veritabanını şişirir
            //   2) nvarchar(max) kolonuna index kurulamaz — Aşama 6'da
            //      açıklamada arama yapacağız, o zaman gerekebilir
            //
            // 2000 karakter: bir ürün açıklaması için fazlasıyla yeterli,
            // roman yazılmasını da engelliyor.
            //
            // ⚠️ Bu sayı ProductCreateDto'daki [MaxLength(2000)] ile
            // AYNI olmalı. Farklı olsalardı ya DTO gereksiz yere
            // reddederdi ya da veritabanı istisna fırlatırdı.
            modelBuilder.Entity<Product>()
                .Property(p => p.Description)
                .HasMaxLength(2000);


            // ⭐ YENİ — barkod benzersiz olsun (aynı barkod iki üründe olamaz).
            // Barcode nullable olduğu için EF, SQL Server'da bu index'e
            // otomatik "WHERE [Barcode] IS NOT NULL" filtresi ekler.
            // Yani barkodu boş (null) olan eski ürünler birbiriyle çakışmaz,
            // sadece DOLU barkodlar tekil olmak zorunda.
            modelBuilder.Entity<Product>()
                .HasIndex(p => p.Barcode)
                .IsUnique();


            // ⭐ YENİ — içe aktarma işi tablosu, kolon boyutlarını düzgün ver
            modelBuilder.Entity<ImportJob>(e =>
            {
                e.Property(x => x.FileName).HasMaxLength(260);
                e.Property(x => x.Status).HasMaxLength(20);
            });

            // ⭐ YENİ — stok hareket defteri
            modelBuilder.Entity<StockMovement>(e =>
            {
                // ⚠️ EN ÖNEMLİ İNDEKS.
                //
                // Bu tablonun ana kullanımı: "şu ürünün hareketleri,
                // en yeniden eskiye". İndeks tam bu sorguya göre
                // kurulmuş.
                //
                // Kolon sırası önemli: önce ProductId (eşitlik
                // filtresi), sonra CreatedAt (sıralama). Ters sırada
                // olsaydı SQL Server tüm tabloyu tarayıp sonra
                // filtrelerdi.
                //
                // ⚠️ Bu tablo diğerlerinden HIZLI büyür: her sipariş
                // kalemi bir satır demek. İndeks olmadan bir yıl
                // sonra ürün detay sayfası açılmazdı.
                e.HasIndex(s => new { s.ProductId, s.CreatedAt });

                // Sebep kısa ve sabit bir metin — nvarchar(max)
                // olmasına gerek yok. Ayrıca ileride bu kolona
                // indeks gerekirse (rapor filtresi) sınırlı
                // uzunluk şart: nvarchar(max) indekslenemez.
                e.Property(s => s.Sebep)
                 .HasMaxLength(30)
                 .IsRequired();

                e.Property(s => s.ReferansTipi)
                 .HasMaxLength(30);

                e.Property(s => s.Aciklama)
                 .HasMaxLength(300);
            });

            // ⭐ YENİ (5.5) — STOK BİLDİRİMLERİ
            modelBuilder.Entity<StockAlert>(e =>
            {
                // ⚠️ AYNI MÜŞTERİ + AYNI ÜRÜN İÇİN TEK SATIR.
                //
                // Kuralı kodda "önce sorgula, yoksa ekle" diye
                // yazabilirdik ama iki istek aynı anda gelirse ikisi de
                // "kayıt yok" cevabı alır ve ikisi de ekler — müşteriye
                // iki mail gider. Kontrol ile yazma arasındaki
                // mikrosaniye her zaman vardır; sepet ve admin
                // başvurularında aynı dersi almıştık.
                //
                // ⚠️ FİLTRESİZ unique — gönderilmiş kayıtlar da dahil.
                // "Sadece bekleyenler benzersiz olsun" deseydik bir
                // müşteri aynı ürün için onlarca kapanmış kayıt
                // biriktirebilirdi. Tekrar abone olmak, mevcut satırın
                // NotifiedAt'ini null'a çekiyor — yeni satır açmıyor.
                e.HasIndex(s => new { s.UserId, s.ProductId })
                 .IsUnique();

                // Tarama sorgusunun indeksi: "bekleyen kayıtları ürün
                // ürün grupla". NotifiedAt önde çünkü eşitlik filtresi
                // (null olanlar) önce daraltıyor.
                e.HasIndex(s => new { s.NotifiedAt, s.ProductId });
            });

            // ⭐ YENİ (4.9) — TELEFON DEFTERİ
            modelBuilder.Entity<Phone>(e =>
            {
                // ⚠️ AYNI MÜŞTERİ AYNI NUMARAYI İKİ KEZ KAYDEDEMEZ.
                //
                // Kodda "önce sorgula, yoksa ekle" diye yazmak
                // yetmezdi: iki istek aynı anda gelirse ikisi de
                // "yok" cevabı alır ve iki satır oluşur. StockAlert,
                // AdminBasvuru ve Order.IdempotencyKey'de alınan
                // dersin aynısı — garantiyi kod değil veritabanı verir.
                //
                // ⚠️ GLOBAL BENZERSİZ DEĞİL, KULLANICI BAZINDA.
                // Bir ailenin ya da bir ortak ofisin aynı numarayı
                // paylaşması tamamen meşru. Global unique koysaydık
                // ikinci kişi kendi numarasını kaydedemez, üstelik
                // hata mesajı "bu numara başkasında kayıtlı" diyerek
                // başka bir hesabın bilgisini sızdırırdı.
                e.HasIndex(p => new { p.UserId, p.Numara })
                 .IsUnique();

                // Kanonik biçim tam 10 hane; 20 pay bırakıyor.
                // ⚠️ Uzunluk şart — nvarchar(max) kolonuna index
                // kurulamaz ve yukarıdaki unique index bu kolonda.
                e.Property(p => p.Numara)
                 .HasMaxLength(20)
                 .IsRequired();

                e.Property(p => p.Etiket)
                 .HasMaxLength(30)
                 .IsRequired();

                // Kullanıcı ile ilişki — Review'daki desenin aynısı:
                // kullanıcı satırı silinmeye kalkılırsa ENGELLE.
                // Hesap kapatma zaten anonimleştirme yapıyor, satırı
                // silmiyor; bu kısıt o kararın veritabanındaki karşılığı.
                e.HasOne<User>()
                 .WithMany()
                 .HasForeignKey(p => p.UserId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ⭐ YENİ (4.9) — ADRES → TELEFON İLİŞKİSİ
            //
            // ⚠️ NEDEN GERÇEK FK? Projede FK'lar tarihsel olarak eksik
            // (Aşama 11'in borcu) çünkü modellerde gezinti özelliği yok
            // ve EF ilişkiyi çıkaramıyor. Burada ilişki AÇIKÇA
            // tanımlanıyor: gezinti özelliği olmadan da FK kurulabilir
            // (Review'da aynısı yapılmıştı). Yeni tablolar borcu
            // büyütmesin.
            //
            // ⚠️ ON DELETE SET NULL — silme davranışı kararı:
            //   • Cascade olsaydı numarayı silen müşteri ADRESİNİ de
            //     kaybederdi. Kimse bunu beklemez.
            //   • Restrict olsaydı numara, ona bağlı adres durdukça
            //     silinemezdi — müşteri çıkmaza girerdi.
            //   • SetNull adresi telefonsuz bırakıyor; sipariş akışı
            //     o adres için yeniden numara seçilmesini istiyor.
            modelBuilder.Entity<Address>()
                .HasOne<Phone>()
                .WithMany()
                .HasForeignKey(a => a.PhoneId)
                .OnDelete(DeleteBehavior.SetNull);

            // ⭐ YENİ (Aşama 8) — DESTEK TALEPLERİ
            modelBuilder.Entity<SupportTicket>(e =>
            {
                // Müşterinin "taleplerim" listesi: kendi talepleri,
                // en son hareket görenden eskiye.
                // Kolon sırası önemli: önce eşitlik filtresi (UserId),
                // sonra sıralama (UpdatedAt).
                e.HasIndex(t => new { t.UserId, t.UpdatedAt });

                // Admin listesi: duruma göre süz, son hareketine göre
                // sırala. Aynı mantık, farklı eşitlik kolonu.
                e.HasIndex(t => new { t.Durum, t.UpdatedAt });

                // ⚠️ nvarchar(max) indekslenemez ve gereksiz yer
                // kaplar; üstelik `Durum` ve `Kategori` filtrede
                // kullanılıyor.
                e.Property(t => t.Konu).HasMaxLength(150).IsRequired();
                e.Property(t => t.Durum).HasMaxLength(20).IsRequired();
                e.Property(t => t.Kategori).HasMaxLength(20).IsRequired();

                // Talebi açan müşteri silinmeye kalkılırsa ENGELLE:
                // yazışma bir kayıttır. (Review'daki desen.)
                e.HasOne<User>()
                 .WithMany()
                 .HasForeignKey(t => t.UserId)
                 .OnDelete(DeleteBehavior.Restrict);

                // ⚠️ Sipariş bağlantısı gerçek FK ama NULLABLE.
                // Restrict: sipariş silinmiyor zaten (ticari kayıt);
                // silinmeye kalkılırsa talep onu tutuyor.
                e.HasOne<Order>()
                 .WithMany()
                 .HasForeignKey(t => t.OrderId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ⭐ YENİ (Aşama 8) — DESTEK MESAJLARI
            modelBuilder.Entity<SupportMessage>(e =>
            {
                // ⚠️ TABLONUN ANA SORGUSU: "şu talebin mesajları,
                // eskiden yeniye". İndeks tam buna göre kurulu.
                // Bu tablo hızlı büyür: her cevap bir satır.
                e.HasIndex(m => new { m.TicketId, m.CreatedAt });

                e.Property(m => m.Mesaj).HasMaxLength(2000).IsRequired();

                // ⚠️ CASCADE — Review'daki ürün ilişkisiyle aynı
                // gerekçe: mesaj talebin PARÇASI, tek başına anlamı
                // yok. Talep bir gün silinirse mesajların ortada
                // kalması sahipsiz satır üretirdi (4.8'de ürün
                // silmenin açtığı hasarın aynısı).
                e.HasOne<SupportTicket>()
                 .WithMany()
                 .HasForeignKey(m => m.TicketId)
                 .OnDelete(DeleteBehavior.Cascade);

                // ⚠️ Gönderen için Restrict: kullanıcı silinse bile
                // yazışma durmalı. Ayrıca EF iki Restrict olmayan yol
                // (Users → Ticket → Message ve Users → Message)
                // çakışırsa "multiple cascade paths" hatası verirdi.
                e.HasOne<User>()
                 .WithMany()
                 .HasForeignKey(m => m.GonderenUserId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ⭐ YENİ — KOMBİNLER
            modelBuilder.Entity<Kombin>(e =>
            {
                e.Property(k => k.Ad).HasMaxLength(100).IsRequired();
                e.Property(k => k.Aciklama).HasMaxLength(300);
            });

            modelBuilder.Entity<KombinUrun>(e =>
            {
                // ⚠️ Aynı ürün bir kombine iki kez eklenemez.
                e.HasIndex(ku => new { ku.KombinId, ku.ProductId }).IsUnique();

                // "Bu ürün hangi kombinlerde?" sorgusu.
                e.HasIndex(ku => ku.ProductId);

                // Kombin silinince kalemleri de gider: kalem tek
                // başına anlamsız.
                e.HasOne<Kombin>()
                 .WithMany()
                 .HasForeignKey(ku => ku.KombinId)
                 .OnDelete(DeleteBehavior.Cascade);

                // Ürün silinmeye kalkılırsa engelle (4.8 zaten
                // silmeyi kapatıyor).
                e.HasOne<Product>()
                 .WithMany()
                 .HasForeignKey(ku => ku.ProductId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ⭐ YENİ (B2) — KAMPANYALAR
            modelBuilder.Entity<Kampanya>(e =>
            {
                e.Property(k => k.Baslik).HasMaxLength(100).IsRequired();
                e.Property(k => k.KisaAciklama).HasMaxLength(200).IsRequired();
                e.Property(k => k.BitisMetni).HasMaxLength(100).IsRequired();
                e.Property(k => k.Aciklama).HasMaxLength(2000).IsRequired();
                e.Property(k => k.GorselUrl).HasMaxLength(300).IsRequired();

                e.Property(k => k.KuponKodlari).HasMaxLength(500).IsRequired();
                e.Property(k => k.Kosullar).HasMaxLength(2000).IsRequired();

                // Müşteri ucunun tek sorgusu bu: yayındakiler, sıraya
                // göre. Kampanya sayısı bir elin parmağı kadar olacak
                // ama indeks sıralamayı da karşıladığı için bedava.
                e.HasIndex(k => new { k.AktifMi, k.Sira });
            });

            // ⭐ YENİ — HIZLI SİPARİŞLER
            modelBuilder.Entity<HizliSiparis>(e =>
            {
                // ⚠️ AYNI SİPARİŞ İKİ KEZ KAYDEDİLEMEZ.
                //
                // Kuralı kodda "önce sorgula, yoksa ekle" diye
                // yazabilirdik ama iki istek aynı anda gelirse ikisi de
                // "yok" cevabı alır ve listede aynı sipariş iki kez
                // görünürdü. Kontrol ile yazma arasındaki mikrosaniye
                // her zaman vardır — Favorites, CartItems ve
                // StockAlerts'te alınan dersin aynısı.
                //
                // ⚠️ Bu indeks aynı zamanda "bu kullanıcının kayıtları"
                // sorgusuna da hizmet ediyor: UserId önde olduğu için
                // liste ucu (sonraki aşama) onu kullanabilecek.
                e.HasIndex(h => new { h.UserId, h.OrderId })
                 .IsUnique();

                // ⚠️ AYNI İÇERİK İKİ KEZ KAYDEDİLEMEZ.
                //
                // Üstteki indeks aynı SİPARİŞİ, bu indeks aynı
                // İÇERİĞİ engelliyor. İkisi farklı sorular: müşteri
                // zeytinyağı siparişini kaydettikten sonra ertesi gün
                // yine zeytinyağı sipariş edip onu da kaydedebiliyordu
                // — farklı sipariş, aynı liste satırı.
                //
                // ⚠️ Kontrolü controller'da "önce sorgula, yoksa ekle"
                // diye de yazabilirdik ama iki eşzamanlı istek ikisi de
                // "yok" görürdü. Garantiyi kod değil veritabanı verir.
                //
                // SHA-256 hex tam 64 karakter.
                e.HasIndex(h => new { h.UserId, h.IcerikImzasi })
                 .IsUnique();

                e.Property(h => h.IcerikImzasi)
                 .HasMaxLength(64)
                 .IsRequired();

                // Kullanıcı silinmiyor (hesap kapatma anonimleştiriyor).
                // Restrict, projedeki diğer kişisel tablolarla aynı
                // karar. ⚠️ Hesap kapatma akışı bu satırları AYRICA
                // siliyor — kişisel tercih verisi, ticari kayıt değil.
                e.HasOne<User>()
                 .WithMany()
                 .HasForeignKey(h => h.UserId)
                 .OnDelete(DeleteBehavior.Restrict);

                // ⚠️ Sipariş ticari kayıt, silinmiyor. Restrict:
                // silinmeye kalkılırsa bu satır onu tutar.
                e.HasOne<Order>()
                 .WithMany()
                 .HasForeignKey(h => h.OrderId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ⭐ YENİ (Aşama 10) — SÖZLEŞMELER
            modelBuilder.Entity<Sozlesme>(e =>
            {
                // ⚠️ Tip başına tek AKTİF sürüm. Filtre olmasaydı aynı
                // tipin ikinci sürümü hiç eklenemezdi.
                e.HasIndex(x => new { x.Tip, x.AktifMi })
                 .IsUnique()
                 .HasFilter("[AktifMi] = 1");

                e.Property(x => x.Tip).HasMaxLength(30).IsRequired();
                e.Property(x => x.Icerik).IsRequired();
            });

            modelBuilder.Entity<SozlesmeOnayi>(e =>
            {
                // "Bu kullanıcı neleri onaylamış?" sorgusu.
                e.HasIndex(x => new { x.UserId, x.OnayTarihi });

                e.Property(x => x.IpAdresi).HasMaxLength(45);   // IPv6 dahil

                e.HasOne<User>()
                 .WithMany()
                 .HasForeignKey(x => x.UserId)
                 .OnDelete(DeleteBehavior.Restrict);

                // ⚠️ Onay hangi SÜRÜME verildi — sözleşme silinemez.
                e.HasOne<Sozlesme>()
                 .WithMany()
                 .HasForeignKey(x => x.SozlesmeId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne<Order>()
                 .WithMany()
                 .HasForeignKey(x => x.OrderId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ⭐ YENİ (Aşama 9) — İADE TALEPLERİ
            modelBuilder.Entity<ReturnRequest>(e =>
            {
                // ⚠️⚠️ AYNI KALEM İÇİN İKİ AÇIK TALEP OLAMAZ.
                //
                // Kuralı kodda "önce sorgula, yoksa ekle" diye
                // yazabilirdik ama iki istek aynı anda gelirse ikisi
                // de "yok" cevabı alır ve müşteri aynı ürün için iki
                // kez para iadesi alabilirdi. Kontrol ile yazma
                // arasındaki mikrosaniye her zaman vardır.
                // (StockAlert, AdminBasvuru, Phone'daki desen.)
                //
                // ⚠️ FİLTRE ŞART: reddedilmiş ya da parası ödenmiş
                // talepler kuralın dışında. Filtresiz olsaydı bir kez
                // reddedilen müşteri o kalem için BİR DAHA hiç talep
                // açamazdı.
                //
                // ⚠️ OrderItemId NULL olanlar (tüm sipariş iadesi)
                // SQL Server'da "birbirine eşit" sayılır — ki tam
                // olarak istediğimiz bu: aynı sipariş için iki tane
                // açık "tümünü iade et" talebi olmasın.
                e.HasIndex(r => new { r.OrderId, r.OrderItemId })
                 .IsUnique()
                 .HasFilter("[Durum] <> 'reddedildi' AND [Durum] <> 'para_iade_edildi'");

                // Admin listesi: duruma göre süz, tarihe göre sırala.
                // Kolon sırası önemli: önce eşitlik, sonra sıralama.
                e.HasIndex(r => new { r.Durum, r.TalepTarihi });

                e.Property(r => r.Durum).HasMaxLength(25).IsRequired();
                e.Property(r => r.Sebep).HasMaxLength(30).IsRequired();
                e.Property(r => r.Aciklama).HasMaxLength(1000);
                e.Property(r => r.RedNedeni).HasMaxLength(500);

                // Para alanı — precision belirtilmezse SQL Server
                // decimal(18,0) yapar ve KURUŞ KAYBOLUR. 12,50 TL
                // veritabanına 13 yazılır ve bunu hiçbir hata mesajı
                // söylemez.
                e.Property(r => r.IadeTutari).HasPrecision(18, 2);

                // Sipariş ve kalem gerçek FK, ikisi de Restrict:
                // ikisi de ticari kayıt, silinmiyorlar.
                e.HasOne<Order>()
                 .WithMany()
                 .HasForeignKey(r => r.OrderId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne<OrderItem>()
                 .WithMany()
                 .HasForeignKey(r => r.OrderItemId)
                 .OnDelete(DeleteBehavior.Restrict);

                // ⚠️ Talebi açan kullanıcı SAKLANMIYOR — `Order.UserId`
                // zaten var ve iade talebi siparişe bağlı. İkinci bir
                // kopya tutsaydık ikisi bir gün ayrışabilirdi.
                // Sahiplik kontrolü sipariş üzerinden yapılıyor.
            });

            // ⭐ YENİ — ADMİN BAŞVURULARI
            modelBuilder.Entity<AdminBasvuru>(e =>
            {
                // ⚠️ EN ÖNEMLİ KISIT — "AYNI ANDA TEK BEKLEYEN BAŞVURU"
                //
                // Bu kuralı kodda "önce sorgula, yoksa ekle" diye
                // yazabilirdik. Ama iki istek aynı anda gelirse ikisi de
                // "bekleyen yok" cevabı alır ve ikisi de kayıt açar.
                // Kontrol ile yazma arasındaki o mikrosaniye her zaman
                // vardır.
                //
                // FİLTRELİ UNIQUE INDEX bunu veritabanı seviyesinde
                // imkânsız kılıyor: bir kullanıcının en fazla BİR tane
                // "beklemede" satırı olabilir.
                //
                // Filtre neden şart? Filtresiz olsaydı kural "bir
                // kullanıcının toplam bir başvurusu olur" haline gelirdi
                // — reddedilen biri bir daha asla başvuramazdı.
                // Karar verilince Durum değişiyor ve slot boşalıyor.
                //
                // Users.Email, Order.OrderNumber ve Order.IdempotencyKey'de
                // uyguladığımız desenin aynısı: garantiyi kod değil
                // veritabanı verir.
                e.HasIndex(b => b.UserId)
                 .IsUnique()
                 .HasFilter("[Durum] = 'beklemede'");

                // Liste ucu duruma göre filtreleyip tarihe göre sıralıyor.
                // Kolon sırası önemli: önce eşitlik filtresi (Durum),
                // sonra sıralama (CreatedAt).
                e.HasIndex(b => new { b.Durum, b.CreatedAt });

                e.Property(b => b.Gerekce)
                 .HasMaxLength(1000)
                 .IsRequired();

                // nvarchar(max) kolonuna index kurulamaz — filtrede
                // kullandığımız için uzunluk şart.
                e.Property(b => b.Durum)
                 .HasMaxLength(20)
                 .IsRequired();

                e.Property(b => b.RedNedeni)
                 .HasMaxLength(500);
            });

            // ⭐ YENİ — REFRESH TOKEN yapılandırması
            modelBuilder.Entity<RefreshToken>(e =>
            {
                // Aramayı hash üzerinden yapacağız (kullanıcı ham token'ı gönderir,
                // biz hash'ler ve bu kolonda ararız). Benzersiz + indeksli olsun:
                //   - Benzersiz: aynı hash iki satırda olamaz (veri bütünlüğü).
                //   - İndeks: milyonlarca satır olsa bile arama şimşek gibi olur.
                e.HasIndex(x => x.TokenHash).IsUnique();

                // SHA-256 hex çıktısı tam 64 karakterdir; kolonu ona göre sınırla.
                e.Property(x => x.TokenHash).HasMaxLength(64);

                // User-agent metni uzun olabilir, rahat bir tavan veriyoruz.
                e.Property(x => x.CihazBilgisi).HasMaxLength(300);

                // Kullanıcı ile ilişki.
                // NEDEN CASCADE — oysa Review'da Restrict kullanmıştık?
                //   Review bir KAYIT/DELİLDİR; kullanıcı gitse bile durması gerekir,
                //   o yüzden orada Restrict (silme). RefreshToken ise sadece OTURUM
                //   verisidir, saklama değeri yoktur; kullanıcı satırı bir gün
                //   gerçekten silinirse bu token'lar da onunla birlikte gitsin.
                e.HasOne<User>()
                 .WithMany()
                 .HasForeignKey(x => x.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
            });


            // ⭐ YENİ — email doğrulama token hash'i SHA-256 hex = 64 karakter
            modelBuilder.Entity<User>()
                .Property(u => u.EmailDogrulamaTokenHash)
                .HasMaxLength(64);


            // ⭐ YENİ — şifre sıfırlama token hash'i de 64 karakter (SHA-256 hex)
            modelBuilder.Entity<User>()
                .Property(u => u.SifreSifirlamaTokenHash)
                .HasMaxLength(64);

            // ⭐ YENİ — profil fotoğrafı yolu.
            //
            // ⚠️ Sınır verilmezse EF nvarchar(max) üretiyor: sayfa dışı
            // saklanan, indekslenemeyen bir sütun. Tuttuğu şey
            // "/uploads/profil/<32 hex>.jpg" — 60 karakteri bile
            // geçmiyor. Kampanya görseliyle aynı sınır (300).
            modelBuilder.Entity<User>()
                .Property(u => u.ProfilFotoUrl)
                .HasMaxLength(300);

            // ⭐ YENİ — E-POSTA BENZERSİZLİĞİ
            //
            // İKİ AYRI AYAR, İKİSİ DE ZORUNLU:
            //
            // 1) HasMaxLength(256)
            //    Sınır belirtilmediğinde EF Core string'i nvarchar(max)
            //    olarak eşliyor. SQL Server nvarchar(max) kolonuna index
            //    KURAMAZ — index anahtarı için üst sınır 1700 bayt ve
            //    max tipinin boyutu belirsiz (2 GB'a kadar).
            //    256 seçtik çünkü RFC 5321 e-posta üst sınırını 254 karakter
            //    olarak tanımlıyor. 256 × 2 bayt = 512 bayt → sınırın çok altı.
            //
            // 2) HasIndex().IsUnique()
            //    Asıl koruma bu. Kayıt olurken AuthController'da AnyAsync ile
            //    kontrol yapıyoruz ama o kontrol ile INSERT arasında bir
            //    boşluk var (TOCTOU). İki istek aynı anda gelirse ikisi de
            //    "yok" görüp ikisi de kaydeder.
            //
            //    Unique index bu boşluğu kapatır: kontrolü uygulama değil
            //    VERİTABANI yapar ve bunu satır kilidi altında, INSERT ile
            //    aynı atomik adımda yapar. Araya girmek fiziksel olarak
            //    mümkün değildir.
            //
            //    Ek fayda: Login/şifre sıfırlamadaki e-posta aramaları da
            //    hızlanır (tam tarama yerine index araması).
            modelBuilder.Entity<User>()
                .Property(u => u.Email)
                .HasMaxLength(256);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // ⭐ YENİ — SEPETTE AYNI ÜRÜN İKİ KEZ OLAMAZ
            //
            // Neden bileşik (composite) indeks?
            //   Kural "bir kullanıcının sepetinde bir ürün en fazla bir satır"
            //   şeklinde. Tek başına UserId benzersiz olamaz (bir kullanıcının
            //   çok ürünü var), tek başına ProductId de olamaz (bir ürün çok
            //   kullanıcının sepetinde). Benzersiz olan İKİLİ.
            //
            // Neden gerekli?
            //   AddToCart'ta "sepette var mı" kontrolü ile INSERT arasında
            //   boşluk var (TOCTOU). İki istek aynı anda gelirse ikisi de
            //   "yok" görüp ikisi de ekler → aynı ürün sepette iki satır.
            //   Müşteri ürünü iki kez görür, birini silse diğeri kalır,
            //   kupon hesabında ürün iki kez sayılır.
            //
            // Ek fayda: (UserId, ProductId) ile yapılan aramalar hızlanır —
            // AddToCart her çağrıldığında tam bu ikiliyle sorgu atıyor.
            //
            // Kolon sırası önemli: UserId önce, çünkü "bir kullanıcının tüm
            // sepeti" sorgusu da bu indeksten faydalanabilir. Tersi olsaydı
            // (ProductId, UserId) o sorgu indeksi kullanamazdı.
            modelBuilder.Entity<CartItem>()
                .HasIndex(c => new { c.UserId, c.ProductId })
                .IsUnique();


            // ⭐ Kupon kodu benzersiz olmalı — aynı kod iki kez oluşturulamaz.
            // Kodu her zaman BÜYÜK harfe çevirip kaydediyoruz, o yüzden
            // "indirim10" ve "INDIRIM10" çakışır (istediğimiz de bu).
            modelBuilder.Entity<Coupon>()
                .HasIndex(c => c.Code)
                .IsUnique();

            // Kullanım kayıtlarında sık sorgulanan alanlar.
            // "Bu kullanıcı bu kuponu kaç kez kullandı?" sorgusu için.
            modelBuilder.Entity<CouponUsage>()
                .HasIndex(cu => new { cu.CouponId, cu.UserId });

            // ⚠️ PARA ALANLARINDA HASSASİYET
            // decimal varsayılan olarak SQL Server'da (18,2) gelmiyor;
            // EF uyarı verir ve yuvarlama sorunları çıkabilir.
            // Para alanlarında hassasiyeti AÇIKÇA belirtmek gerekir.
            modelBuilder.Entity<Coupon>()
                .Property(c => c.DiscountValue)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Coupon>()
                .Property(c => c.MinOrderAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Coupon>()
                .Property(c => c.MaxDiscountAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CouponUsage>()
                .Property(cu => cu.DiscountAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Order>()
                .Property(o => o.SubTotal)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Order>()
                .Property(o => o.DiscountAmount)
                .HasPrecision(18, 2);

            // ⭐ YENİ — kargo ücreti hassasiyeti.
            //
            // ⚠️ Bu satır Aşama 4.2'de atlanmıştı. Kolon yine de
            // decimal(18,2) olarak oluştu — EF Core'un SQL Server
            // varsayılanı bu — yani veride bir sorun YOK.
            //
            // Sorun şu: EF her migration'da "ShippingCost için tip
            // belirtilmemiş, değerler sessizce kırpılabilir" uyarısı
            // basıyordu. Zararsız bir uyarının sürekli görünmesi, bir
            // gün çıkacak GERÇEK uyarıyı gürültüde kaybettirir.
            //
            // Projedeki diğer 12 para kolonu bunu açıkça bildiriyor;
            // tek istisna olarak bırakmanın da bir gerekçesi yok.
            modelBuilder.Entity<Order>()
                .Property(o => o.ShippingCost)
                .HasPrecision(18, 2);

            // ⭐ YENİ — sepete eklenme fiyatı da bir para değeri.
            // Projedeki 13 para kolonunun hepsi bunu açıkça bildiriyor;
            // bildirmeyenler EF uyarısı üretiyor (bkz. ShippingCost).
            modelBuilder.Entity<CartItem>()
                .Property(c => c.EklenmeFiyati)
                .HasPrecision(18, 2);


            // ⭐ YENİ — KARGO VE NOT ALANLARININ UZUNLUK SINIRLARI
            //
            // NEDEN GEREKLİ?
            // Yapılandırma vermezsek EF Core, string alanları SQL Server'da
            // NVARCHAR(MAX) olarak oluşturur. Bu üç sorun doğurur:
            //
            //   1) NVARCHAR(MAX) satır dışında ("off-row") saklanır —
            //      okuma yavaşlar.
            //   2) MAX kolonlara indeks kurulamaz. Şimdi gerekmiyor ama
            //      "takip numarasına göre sipariş bul" istenirse elimiz
            //      bağlı kalır.
            //   3) Sınır yoksa bir hata (ya da kötü niyet) 2 GB'lık bir
            //      metni veritabanına yazabilir.
            //
            // Sınırı VERİTABANI seviyesinde koymak, DTO'daki [MaxLength]
            // doğrulamasının YEDEĞİdir. DTO doğrulaması atlanabilir
            // (yeni bir endpoint yazarken unutulur, arka plan işi
            // doğrudan modele yazar); veritabanı sınırı atlanamaz.
            // Aynı doğrulamanın iki katmanda olması tekrar değil,
            // savunma derinliğidir.

            // 50: en uzun firma adı bile 20 karakteri geçmiyor,
            // rahat bir tavan bırakıyoruz.
            modelBuilder.Entity<Order>()
                .Property(o => o.ShippingCompany)
                .HasMaxLength(50);

            // 50: takip numaraları tipik olarak 10-20 karakter.
            modelBuilder.Entity<Order>()
                .Property(o => o.TrackingNumber)
                .HasMaxLength(50);

            // 500: "kapıya bırakın" tarzı notlar için fazlasıyla yeterli.
            // Sınırsız bırakmak müşterinin roman yazmasına ve kargo
            // etiketine sığmayan bir metne yol açardı.
            modelBuilder.Entity<Order>()
                .Property(o => o.CustomerNote)
                .HasMaxLength(500);


            // ⭐ YENİ — YORUM METNİ UZUNLUK SINIRI
            //
            // Bu kolon tek istisnaydı: projedeki diğer bütün metin
            // alanlarına sınır konmuş (ProductName 200, Description 2000,
            // SupportMessage.Mesaj 2000, CustomerNote 500...), Comment ise
            // nvarchar(max) olarak kalmıştı.
            //
            // ⚠️ 1000, ReviewCreateDto'daki [StringLength(1000)] ile AYNI
            // sayı olmak zorunda. Farklı olsalardı ya DTO gereksiz yere
            // reddederdi ya da veritabanı ham bir istisna fırlatırdı.
            modelBuilder.Entity<Review>()
                .Property(r => r.Comment)
                .HasMaxLength(1000);


            // ============================================================
            //  ⭐ YENİ — EKSİK İNDEKSLER
            //
            //  ⚠️ NEDEN TOPLU BİR BLOK?
            //
            //  Yeni tablolarda (StockMovements, SupportTickets,
            //  ReturnRequests, Phones...) indeksler kolon sırasına kadar
            //  düşünülmüştü. Eski tablolar ise hiç gözden geçirilmemişti:
            //  OrderItems, Payments, ProductImages, Favorites, Addresses,
            //  Cards ve AuditLogs'ta HİÇ indeks yoktu.
            //
            //  ⚠️ FK ≠ İNDEKS. Yaygın bir yanılgı: SQL Server yabancı
            //  anahtar tanımlayınca o kolona otomatik indeks KURMAZ.
            //  Bu projede FK'ların çoğu zaten yok, indeksleri de yoktu.
            //
            //  KOLON SIRASI İLKESİ (bu dosyada zaten uygulanıyor):
            //  önce EŞİTLİK filtresi, sonra SIRALAMA. Ters sırada olsaydı
            //  SQL Server tabloyu tarayıp sonra filtrelerdi.
            // ============================================================

            // ⚠️ EN SICAK İNDEKS. OrderItems bu sistemin en çok
            // sorgulanan tablosu: sipariş detayı, mail kalemleri,
            // raporlar, "siparişi tekrarla", yorum uygunluğu, ürün
            // silinebilirliği ve iade akışı hep OrderId ile filtreliyor.
            // İndeks yokken hepsi tam tablo taramasıydı.
            modelBuilder.Entity<OrderItem>()
                .HasIndex(oi => oi.OrderId);

            // "Bu ürün hangi siparişlerde geçti?" — satış raporu,
            // popülerlik sıralaması ve silinebilirlik kontrolü.
            modelBuilder.Entity<OrderItem>()
                .HasIndex(oi => oi.ProductId);

            // ⚠️ "Siparişlerim" ekranı BUNSUZ tam tarama yapıyordu.
            //
            // Orders'ta UserId geçen tek indeks (UserId, IdempotencyKey)
            // ama o FİLTRELİ (IdempotencyKey IS NOT NULL). SQL Server
            // filtreli bir indeksi, filtre kolonunu içermeyen bir sorgu
            // için kullanamaz — yani o indeks buraya hiç yardım etmiyordu.
            //
            // CreatedAt ikinci kolon çünkü liste tarihe göre sıralanıyor.
            modelBuilder.Entity<Order>()
                .HasIndex(o => new { o.UserId, o.CreatedAt });

            // Sipariş detayı ve iptal akışı ödemeleri OrderId ile çekiyor.
            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.OrderId);

            // "Ödemelerim" listesi: kendi ödemeleri, tarihe göre sıralı.
            modelBuilder.Entity<Payment>()
                .HasIndex(p => new { p.UserId, p.PaidAt });

            // ⚠️ HER ÜRÜN LİSTESİNDE VE HER SEPET GÖRÜNTÜLEMESİNDE
            // çalışan sorgu. Üç kolon da sorgunun kendisinden geliyor:
            // ProductId ile filtrele, IsMain azalan + SortOrder artan
            // sırala. Üçü indekste olunca SQL Server ayrıca sıralama
            // yapmak zorunda kalmıyor.
            modelBuilder.Entity<ProductImage>()
                .HasIndex(pi => new { pi.ProductId, pi.IsMain, pi.SortOrder });

            // ⚠️ BENZERSİZ — hem indeks hem YARIŞ KORUMASI.
            //
            // FavoritesController "önce sorgula, yoksa ekle" yapıyordu;
            // iki eşzamanlı istek ikisi de "yok" görüp iki satır
            // ekleyebilirdi. CartItems, StockAlerts ve Phones'ta alınan
            // dersin aynısı: garantiyi kod değil veritabanı verir.
            //
            // ⚠️ Benzersiz yapmadan önce mevcut mükerrer satırlar
            // tarandı (0 çıktı). Mükerrer olsaydı migration patlardı.
            modelBuilder.Entity<Favorite>()
                .HasIndex(f => new { f.UserId, f.ProductId })
                .IsUnique();

            // Adres ve kart listeleri kullanıcı bazında çekiliyor;
            // ayrıca sipariş akışı ikisini de sahiplik kontrolüyle
            // sorguluyor.
            modelBuilder.Entity<Address>()
                .HasIndex(a => a.UserId);

            modelBuilder.Entity<Card>()
                .HasIndex(c => c.UserId);

            // Kategori filtresi — mobil kategori ekranının ana sorgusu.
            modelBuilder.Entity<Product>()
                .HasIndex(p => p.CategoryId);

            // Denetim kaydı sayfası tarih aralığına göre süzüp
            // tarihe göre sıralıyor.
            modelBuilder.Entity<AuditLog>()
                .HasIndex(l => l.CreatedAt);

            // ⭐ YENİ — istemci adresi. 45 karakter: IPv6'nın en uzun
            // hâli 39, IPv4-eşlenmiş biçim (::ffff:192.168.1.1) ile
            // birlikte 45'i geçmiyor. 15 yazmak IPv6'yı sessizce kırpardı.
            // (SozlesmeOnayi.IpAdresi ile aynı sınır.)
            modelBuilder.Entity<AuditLog>()
                .Property(l => l.IpAdresi)
                .HasMaxLength(45);


            // ============================================================
            //  ⭐ YENİ — SİSTEM KAYITLARI (log tabloları)
            //
            //  ⚠️ Üçünde de CreatedAt indeksi var ve İKİ İŞİ birden
            //  görüyor: ekrandaki tarih süzgeci ve gece temizliği
            //  (WHERE CreatedAt < x). İndekssiz temizlik her gece tüm
            //  tabloyu tarardı.
            //
            //  ⚠️ SPEKÜLATİF İNDEKS YOK. Olay/sonuç kolonlarına indeks
            //  eklenmedi: her indeks INSERT'i yavaşlatır ve bunlar
            //  ağırlıklı YAZILAN tablolar. Filtre gerçekten yavaşlarsa
            //  o zaman eklenir.
            // ============================================================

            modelBuilder.Entity<EmailKaydi>(e =>
            {
                e.HasIndex(x => x.CreatedAt);

                e.Property(x => x.Alici).HasMaxLength(256).IsRequired();
                e.Property(x => x.Konu).HasMaxLength(250).IsRequired();

                // Olay adı bazen ekleniyor ("SiparisDurumu:kargoda"),
                // 60 rahat bir tavan.
                e.Property(x => x.Olay).HasMaxLength(60).IsRequired();

                e.Property(x => x.HataMesaji).HasMaxLength(1000);
                e.Property(x => x.SaglayiciMesajId).HasMaxLength(120);

                // ⚠️ GovdeHtml BİLEREK sınırsız (nvarchar(max)): mail
                // gövdesi uzun ve yalnızca başarısız kayıtlarda dolu.
                // Sınır koymak, tekrar gönderilecek maili kırpmak olurdu.
            });

            modelBuilder.Entity<GirisKaydi>(e =>
            {
                e.HasIndex(x => x.CreatedAt);

                e.Property(x => x.Email).HasMaxLength(256).IsRequired();
                e.Property(x => x.Sonuc).HasMaxLength(30).IsRequired();
                e.Property(x => x.IpAdresi).HasMaxLength(45);
            });

            modelBuilder.Entity<HataKaydi>(e =>
            {
                e.HasIndex(x => x.CreatedAt);

                e.Property(x => x.Yol).HasMaxLength(300).IsRequired();
                e.Property(x => x.Yontem).HasMaxLength(10).IsRequired();
                e.Property(x => x.Mesaj).HasMaxLength(1000).IsRequired();
                e.Property(x => x.IpAdresi).HasMaxLength(45);

                // ⚠️ YiginIzi sınırsız — kırpmak kök sebebi kesebilir.

                // ⚠️ KullaniciId GERÇEK FK DEĞİL. Hata anında kullanıcı
                // satırı silinmiş ya da token bayat olabilir; FK olsaydı
                // hata kaydını yazma denemesi de patlardı — hata
                // döngüsünün ta kendisi.
            });

        }
    }
}