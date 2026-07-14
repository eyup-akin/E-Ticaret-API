namespace ETicaretAPI.Models
{
    public class User
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;

        // customer | admin | superadmin
        public string Role { get; set; } = "customer";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ⭐ SOFT DELETE — kullanıcı ASLA silinmez, pasifleştirilir.
        // Silseydik: siparişleri yetim kalırdı, ciro raporu bozulurdu,
        // "bu siparişi kim verdi?" sorusunun cevabı kaybolurdu.
        public bool IsActive { get; set; } = true;

        // ⭐ GÜVENLİK DAMGASI — token bayatlamasını çözer.
        // Rol değişince / pasifleşince bu damga YENİLENİR.
        // Elindeki eski token'ın damgası artık tutmaz → anında geçersiz olur.
        public string SecurityStamp { get; set; } = Guid.NewGuid().ToString();
    }
}