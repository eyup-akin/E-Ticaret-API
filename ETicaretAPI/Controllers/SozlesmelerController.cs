using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ETicaretAPI.Data;
using ETicaretAPI.DTOs;
using ETicaretAPI.Models;

namespace ETicaretAPI.Controllers
{
    // ⭐ YENİ (Aşama 10) — SÖZLEŞME METİNLERİ
    //
    // Herkese açık: onay kutusunun yanındaki metne kayıt olmadan da
    // bakılabilmeli.
    [Route("api/[controller]")]
    [ApiController]
    public class SozlesmelerController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SozlesmelerController(AppDbContext context)
        {
            _context = context;
        }

        // 🟢 GET /api/sozlesmeler — aktif metinlerin listesi (içeriksiz)
        [HttpGet]
        public async Task<IActionResult> Liste()
        {
            // ⚠️ İçerik gönderilmiyor: liste ucu özet, detay ucu tam veri.
            var liste = await _context.Sozlesmeler
                .Where(s => s.AktifMi)
                .OrderBy(s => s.Tip)
                .Select(s => new { s.Id, s.Tip, s.Surum, s.YayinTarihi })
                .ToListAsync();

            return Ok(liste);
        }

        // 🟢 GET /api/sozlesmeler/gizlilik — aktif sürümün tam metni
        [HttpGet("{tip}")]
        public async Task<IActionResult> Getir(string tip)
        {
            if (!SozlesmeTipi.Gecerliler.Contains(tip))
            {
                return NotFound(new { mesaj = "Böyle bir sözleşme yok." });
            }

            var sozlesme = await _context.Sozlesmeler
                .Where(s => s.Tip == tip && s.AktifMi)
                .Select(s => new { s.Id, s.Tip, s.Surum, s.Icerik, s.YayinTarihi })
                .FirstOrDefaultAsync();

            if (sozlesme == null)
            {
                return NotFound(new { mesaj = "Bu sözleşmenin yayında bir sürümü yok." });
            }

            return Ok(sozlesme);
        }


        // ============================================================
        //  ⭐ YENİ — YÖNETİM UÇLARI (SADECE SÜPERADMİN)
        //
        //  ⚠️ NEDEN CONTROLLER SEVİYESİNDE DEĞİL, UÇ SEVİYESİNDE
        //  YETKİ? Yukarıdaki iki uç HERKESE AÇIK olmak zorunda
        //  (onay kutusunun yanındaki metne kayıt olmadan bakılabilmeli).
        //  Bu yüzden [Authorize] her yönetim ucunun kendi başında
        //  duruyor — mutlak rota (/api/admin/...) ile birlikte.
        //
        //  ⚠️ "admin" YETMİYOR, "superadmin" ŞART. Sözleşme mağazanın
        //  yasal taahhüdü; sipariş durumu değiştirmekle aynı ağırlıkta
        //  değil. Menü ve rota zaten süperadmine kilitli ama gerçek
        //  kilit burası — diğer ikisi yalnızca yanlış yere gitmeyi
        //  önleyen nezaket.
        // ============================================================

        // 🟣 GET /api/admin/sozlesmeler/gizlilik/surumler — sürüm geçmişi
        //
        // ⚠️ İÇERİK YOK, ÖZET VAR. Geçmişin buradaki işi "eski metin
        // duruyor mu" sorusuna cevap vermek; on sürümün tam metnini
        // taşımak listeyi ağırlaştırırdı.
        [Authorize(Roles = "superadmin")]
        [HttpGet("/api/admin/sozlesmeler/{tip}/surumler")]
        public async Task<IActionResult> Surumler(string tip)
        {
            if (!SozlesmeTipi.Gecerliler.Contains(tip))
            {
                return NotFound(new { mesaj = "Böyle bir sözleşme yok." });
            }

            var surumler = await _context.Sozlesmeler
                .Where(s => s.Tip == tip)
                .OrderByDescending(s => s.Surum)
                .Select(s => new
                {
                    s.Id,
                    s.Surum,
                    s.YayinTarihi,
                    s.AktifMi,

                    // ⚠️ Bu sayı, sürümlemenin NEDEN var olduğunu
                    // gösteren tek rakam: eski metne verilmiş onaylar
                    // duruyor ve yeni sürüm onları taşımıyor.
                    onaySayisi = _context.SozlesmeOnaylari.Count(o => o.SozlesmeId == s.Id)
                })
                .ToListAsync();

            return Ok(surumler);
        }


        // 🟣 PUT /api/admin/sozlesmeler/gizlilik — YENİ SÜRÜM YAYINLA
        //
        // ⚠️ METİN GÜNCELLENMİYOR, YENİ SÜRÜM AÇILIYOR.
        //
        // Mevcut satırın Icerik alanını değiştirseydik, o metne verilmiş
        // bütün onaylar sessizce BAŞKA bir metne bağlanırdı: müşteri
        // hiç görmediği bir sözleşmeyi onaylamış görünürdü. İspat
        // değerinin tamamı burada kaybolur.
        //
        // Bunun yerine: eski sürüm pasifleşiyor (metni ve onayları
        // olduğu gibi duruyor), yeni sürüm aktif oluyor. Sipariş
        // dondurma prensibinin aynısı.
        //
        // ⚠️ ESKİ ONAYLAR YENİLENMİYOR. Yeni sürüm yayınlandığında
        // mevcut müşteriler otomatik olarak onu onaylamış SAYILMIYOR;
        // yeni kayıt ve siparişler yeni sürüme onay veriyor. Geçmiş
        // müşterilerden tekrar onay toplamak ayrı bir iş (ekranda
        // uyarı olarak yazılı).
        [Authorize(Roles = "superadmin")]
        [HttpPut("/api/admin/sozlesmeler/{tip}")]
        public async Task<IActionResult> Guncelle(string tip, [FromBody] SozlesmeGuncelleDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (!SozlesmeTipi.Gecerliler.Contains(tip))
            {
                return NotFound(new { mesaj = "Böyle bir sözleşme yok." });
            }

            // ---- KAPI 1: ELLE YAZILAN ONAY ----
            //
            // ⚠️ Karşılaştırma Trim'li ama büyük/küçük harfe DUYARLI
            // ve Türkçe kültüre bağlı DEĞİL (Ordinal): "ONAYLIYORUM"
            // kelimesindeki I harfi, tr-TR karşılaştırmasında ı/i
            // sürprizleri üretebilir. Sabit bir parola gibi davranıyor.
            if (!string.Equals(dto.Dogrulama.Trim(), OnayKelimesi, StringComparison.Ordinal))
            {
                return BadRequest(new
                {
                    mesaj = $"Onay kutusuna tam olarak '{OnayKelimesi}' yazmalısın."
                });
            }

            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var kullanici = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (kullanici == null)
            {
                return Unauthorized(new { mesaj = "Oturum sahibi bulunamadı." });
            }

            // ---- KAPI 2: ŞİFRE ----
            //
            // ⚠️ 401 DEĞİL 400 — hesap kapatmadaki desenin aynısı.
            // 401 dönseydi ön yüzdeki api katmanı bunu "access token
            // bayatladı" sanıp önce yenilemeyi dener, sonra oturumu
            // kapatırdı: süperadmin şifreyi yanlış yazdı diye panelden
            // atılırdı. Oturum geçerli, reddedilen İŞLEM.
            if (!BCrypt.Net.BCrypt.Verify(dto.Sifre, kullanici.PasswordHash))
            {
                return BadRequest(new { mesaj = "Şifre doğrulanamadı. Sözleşme değiştirilmedi." });
            }

            var aktif = await _context.Sozlesmeler
                .FirstOrDefaultAsync(s => s.Tip == tip && s.AktifMi);

            if (aktif == null)
            {
                return NotFound(new { mesaj = "Bu sözleşmenin yayında bir sürümü yok." });
            }

            // ---- KAPI 3: EKRANDAKİ SÜRÜM HÂLÂ GÜNCEL Mİ? ----
            if (dto.BeklenenSurum != aktif.Surum)
            {
                return Conflict(new
                {
                    mesaj = $"Bu metin sen düzenlerken değişti (yayındaki sürüm v{aktif.Surum}). " +
                            "Sayfayı yenileyip değişikliğini yeni metnin üstüne uygula."
                });
            }

            var yeniIcerik = dto.Icerik.Trim();

            // ⚠️ Aynı metin yeni sürüm AÇMAZ. Açsaydık geçmiş, birbirinin
            // kopyası sürümlerle dolar ve "hangi sürümde ne değişti"
            // sorusu cevapsız kalırdı.
            if (yeniIcerik == aktif.Icerik.Trim())
            {
                return BadRequest(new { mesaj = "Metinde bir değişiklik yok." });
            }

            // ⚠️ AÇIK TRANSACTION ŞART.
            //
            // Tip başına yalnızca BİR aktif sürüm olabilir (filtreli
            // benzersiz indeks). Eskiyi pasifleştirmek ile yeniyi
            // eklemek arasında hata olursa mağaza o tipte METİNSİZ
            // kalır — müşteri kayıt olurken okuyacak sözleşme bulamaz.
            //
            // ⚠️ Sıra da şart: önce pasifleştir, sonra ekle. Tersi
            // indeksi ihlal ederdi.
            await using var islem = await _context.Database.BeginTransactionAsync();

            aktif.AktifMi = false;
            await _context.SaveChangesAsync();

            // ⚠️ Sürüm numarası MAKSİMUMDAN türetiliyor, "aktif + 1"
            // değil: geçmişte bir sürüm geri alınmışsa aktif olan en
            // yüksek numara olmayabilir ve numara tekrar ederdi.
            var enYuksekSurum = await _context.Sozlesmeler
                .Where(s => s.Tip == tip)
                .MaxAsync(s => s.Surum);

            var yeni = new Sozlesme
            {
                Tip = tip,
                Surum = enYuksekSurum + 1,
                Icerik = yeniIcerik,
                YayinTarihi = DateTime.UtcNow,
                AktifMi = true
            };

            _context.Sozlesmeler.Add(yeni);

            // ⚠️ DENETİM KAYDI — bu işlemin geri alınamayan tarafı.
            //
            // TargetUserId işlemi YAPANIN kendisi: denetim kaydının
            // hedef alanı bir kullanıcıya işaret etmek zorunda ve
            // burada değişen şey bir kullanıcı değil. Hedef adı, neyin
            // değiştiğini okunur biçimde taşıyor.
            _context.AuditLogs.Add(new AuditLog
            {
                ActorUserId = userId,
                ActorName = kullanici.FullName,
                TargetUserId = userId,
                TargetName = $"Sözleşme: {tip}",
                Action = "sozlesme_guncellendi",
                OldValue = $"v{aktif.Surum}",
                NewValue = $"v{yeni.Surum}",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            await islem.CommitAsync();

            return Ok(new
            {
                mesaj = $"Yeni sürüm yayınlandı (v{yeni.Surum}). Eski metin ve ona verilmiş onaylar korundu.",
                surum = yeni.Surum,
                yayinTarihi = yeni.YayinTarihi
            });
        }


        // Elle yazılması istenen onay kelimesi.
        //
        // ⚠️ Sunucuda sabit: ekrandaki metinden okusaydık, isteği elle
        // atan biri için hiçbir engel kalmazdı.
        private const string OnayKelimesi = "ONAYLIYORUM";
    }
}
