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

        // ⭐ YENİ — BRUTE-FORCE KİLİDİ
        // Üst üste kaç yanlış şifre geldi (doğru girişte 0'a döner).
        public int YanlisGirisSayisi { get; set; } = 0;

        // Hesap ne zamana kadar kilitli? null = kilit yok.
        // Süre GELECEKTEyse giriş reddedilir; GEÇMİŞteyse kilit kalkmış demektir.
        public DateTime? KilitBitis { get; set; }


        // ⭐ YENİ — EMAIL DOĞRULAMA
        // Yeni kayıtlar doğrulanmamış (false) başlar; linke tıklayınca true olur.
        public bool EmailDogrulandiMi { get; set; } = false;

        // Doğrulama token'ının HASH'i. Ham token maille gider; refresh'te olduğu
        // gibi burada da yalnızca hash saklarız (DB sızsa link kullanılamasın).
        public string? EmailDogrulamaTokenHash { get; set; }

        // Token ne zamana kadar geçerli (24 saat vereceğiz).
        public DateTime? EmailDogrulamaTokenBitis { get; set; }

        // ⭐ YENİ — ŞİFRE SIFIRLAMA
        // Aynı mantık: ham token maille gider, DB'de yalnızca HASH durur.
        // ⭐ YENİ — PROFİL FOTOĞRAFI
        //
        // "/uploads/profil/a3f9c1.jpg" — yoksa null.
        //
        // ⚠️ NULL = FOTOĞRAF YOK, boş metin değil. Ekran null görünce
        // baş harfli daireyi çiziyor; boş metin olsaydı "src=''" ile
        // kırık bir görsel denenirdi.
        //
        // ⚠️ HESAP KAPATILINCA TEMİZLENİYOR (HesabimiSil). Ad ve
        // e-posta maskeleniyor ama fotoğraf diskte kalsaydı, kimliği
        // silinmiş bir kaydın yüzü sunucuda durmaya devam ederdi —
        // maskelemenin amacını boşa çıkarırdı.
        public string? ProfilFotoUrl { get; set; }

        public string? SifreSifirlamaTokenHash { get; set; }

        // Sıfırlama linki ne zamana kadar geçerli (1 saat vereceğiz —
        // şifre işlemi hassas olduğu için doğrulamadan daha kısa tutuyoruz).
        public DateTime? SifreSifirlamaTokenBitis { get; set; }


    }
}