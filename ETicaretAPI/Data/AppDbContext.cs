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

        }
    }
}