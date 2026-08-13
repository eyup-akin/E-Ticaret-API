using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;

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
    public class DenetimKaydi
    {
        private readonly AppDbContext _context;

        public DenetimKaydi(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Denetim kaydı ekler (context'e EKLER, kaydetmez).
        /// </summary>
        /// <param name="yapanId">İşlemi yapan (token'dan okunmuş) kullanıcı.</param>
        /// <param name="hedefId">İşlemden etkilenen kullanıcı.</param>
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
    // (DenetimKaydiSayfasi.jsx). Eşleşmezse ekran çökmez — ham kodu
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
    }
}
