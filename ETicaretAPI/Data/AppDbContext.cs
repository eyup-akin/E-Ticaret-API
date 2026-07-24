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

        }
    }
}