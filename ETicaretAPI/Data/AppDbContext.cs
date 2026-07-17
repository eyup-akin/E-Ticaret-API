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
        }
    }
}