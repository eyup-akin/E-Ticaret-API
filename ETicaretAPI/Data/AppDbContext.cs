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
        public DbSet<Card> Cards { get; set; }              // YENİ
        public DbSet<CartItem> CartItems { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Payment> Payments { get; set; }        // YENİ
        public DbSet<ProductImage> ProductImages { get; set; }  // YENİ
        public DbSet<Favorite> Favorites { get; set; }

        public DbSet<AuditLog> AuditLogs { get; set; }  // YENİ

        public DbSet<Review> Reviews { get; set; }  // YENİ - yorumlar tablosu


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Bir kullanıcı bir ürüne YALNIZCA BİR yorum yapabilsin (veritabanı garantisi)
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


            // ⭐ DECIMAL PRECISION — para alanları tam 18 basamak / 2 kuruş.
            // Belirtmezsek EF uyarı verir; varsayılan hassasiyet kuruş kaybına yol açabilir.
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

        }


    }




}