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
        public DbSet<ImportJob> ImportJobs { get; set; } // ⭐ YENİ

        public DbSet<RefreshToken> RefreshTokens { get; set; } // ⭐ YENİ

        public DbSet<Coupon> Coupons { get; set; }
        public DbSet<CouponUsage> CouponUsages { get; set; }

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

        }
    }
}