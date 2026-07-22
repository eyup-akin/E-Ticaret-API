using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;


using Microsoft.AspNetCore.RateLimiting; // ⭐ YENİ

namespace ETicaretAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ETicaretAPI.Services.TokenService _tokenService;

        // Refresh token ömrü — tek yerden değiştirebilelim diye sabit.
        private const int RefreshGunSayisi = 30;


        private const int MaxYanlisDeneme = 5;  // ⭐ YENİ — kaç yanlıştan sonra kilit
        private const int KilitDakika = 15;     // ⭐ YENİ — kilit süresi (dakika)

        private const int EmailTokenSaat = 24;  // ⭐ YENİ — doğrulama linki 24 saat geçerli

        private const int SifirlamaSaat = 1;     // ⭐ YENİ — sıfırlama linki 1 saat geçerli


        private readonly ETicaretAPI.Services.IEmailGonderici _email;
        private readonly IConfiguration _config;

        public AuthController(
            AppDbContext context,
            ETicaretAPI.Services.TokenService tokenService,
            ETicaretAPI.Services.IEmailGonderici email,   // ⭐ YENİ
            IConfiguration config)                        // ⭐ YENİ
        {
            _context = context;
            _tokenService = tokenService;
            _email = email;     // ⭐ YENİ
            _config = config;   // ⭐ YENİ
        }

        // POST /api/auth/register
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            var emailVarMi = await _context.Users.AnyAsync(u => u.Email == dto.Email);
            if (emailVarMi)
                return BadRequest(new { mesaj = "Bu email zaten kayıtlı biladerim!" });

            var hashlenmisSifre = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            // ⭐ Doğrulama token'ı üret. RefreshTokenUret zaten güvenli-rastgele
            // opaque bir metin üretiyor; aynısını burada da kullanıyoruz.
            var hamToken = _tokenService.RefreshTokenUret();

            var yeniKullanici = new User
            {
                FullName = dto.FullName,
                Email = dto.Email,
                PasswordHash = hashlenmisSifre,
                Role = "customer",

                // ⭐ Doğrulanmamış başlar; linke tıklayınca açılacak
                EmailDogrulandiMi = false,
                EmailDogrulamaTokenHash = _tokenService.Hashle(hamToken), // sadece HASH saklanır
                EmailDogrulamaTokenBitis = DateTime.UtcNow.AddHours(EmailTokenSaat)
            };

            _context.Users.Add(yeniKullanici);
            await _context.SaveChangesAsync();

            // ⭐ Doğrulama linkini kur ve (dev göndericiyle) gönder.
            // HAM token linke gider; DB'de yalnızca hash var → link sızsa bile
            // DB'den geri üretilemez.
            var tabanUrl = _config["Uygulama:TabanUrl"];
            var link = $"{tabanUrl}/api/auth/verify-email?token={Uri.EscapeDataString(hamToken)}";

            var govde =
                $"<p>Merhaba {yeniKullanici.FullName},</p>" +
                $"<p>Hesabını doğrulamak için aşağıdaki linke tıkla (24 saat geçerli):</p>" +
                $"<p><a href=\"{link}\">{link}</a></p>";

            await _email.GonderAsync(yeniKullanici.Email, "Email Doğrulama", govde);

            return Ok(new { mesaj = "Kayıt başarılı! Lütfen email adresine gelen linkle hesabını doğrula." });
        }


        // GET /api/auth/verify-email?token=xxxx
        // Kullanıcı maildeki linke TARAYICIDA tıklıyor.
        //
        // ⭐ DEĞİŞTİ: Artık JSON değil HTML döndürüyoruz. Eskiden kullanıcı
        // ekranda çıplak {"mesaj":"..."} görüyordu. Bu endpoint'i insan
        // tıklıyor, program değil — o yüzden insan formatında cevap veriyoruz.
        [HttpGet("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return DogrulamaSayfasi("Geçersiz Link",
                    "Doğrulama linki eksik ya da bozuk. Linkin tamamını kopyaladığından emin ol.", false);

            // Ham token'ı hash'leyip eşleşen kullanıcıyı bul (DB'de hash duruyor).
            var hash = _tokenService.Hashle(token);
            var kullanici = await _context.Users
                .FirstOrDefaultAsync(u => u.EmailDogrulamaTokenHash == hash);

            if (kullanici == null)
                return DogrulamaSayfasi("Geçersiz Link",
                    "Bu doğrulama linki geçerli değil. Uygulamadan yeni bir link isteyebilirsin.", false);

            // Zaten doğrulanmışsa (linke ikinci kez tıklandıysa) dostça karşıla.
            if (kullanici.EmailDogrulandiMi)
                return DogrulamaSayfasi("Hesabın Zaten Doğrulanmış",
                    "Uygulamaya dönüp giriş yapabilirsin.", true);

            // Süre dolmuş mu?
            if (kullanici.EmailDogrulamaTokenBitis == null ||
                kullanici.EmailDogrulamaTokenBitis < DateTime.UtcNow)
                return DogrulamaSayfasi("Linkin Süresi Dolmuş",
                    "Bu link 24 saat geçerliydi. Uygulamadaki giriş ekranından yeni bir link isteyebilirsin.", false);

            // ✅ Doğrula ve token'ı temizle (tek kullanımlık — tekrar kullanılamasın).
            kullanici.EmailDogrulandiMi = true;
            kullanici.EmailDogrulamaTokenHash = null;
            kullanici.EmailDogrulamaTokenBitis = null;
            await _context.SaveChangesAsync();

            return DogrulamaSayfasi("Email Adresin Doğrulandı",
                "Hesabın hazır! Uygulamaya dönüp giriş yapabilirsin.", true);
        }



        // POST /api/auth/resend-verification  { "email": "..." }
        // Doğrulama linkini kaybeden veya süresi dolan kullanıcı için yeni link üretir.
        //
        // NEDEN ŞART? Email doğrulama zorunlu olduğu an şu delik açılıyor:
        // kullanıcı kayıt oldu ama maili kaybetti → giriş yapamıyor,
        // yeniden kayıt olamıyor (email dolu), şifre sıfırlama işe yaramıyor
        // (şifresini biliyor). Bu endpoint olmadan hesap ölü kalıyor.
        [EnableRateLimiting("eposta")]
        [HttpPost("resend-verification")]
        public async Task<IActionResult> ResendVerification([FromBody] EmailIstekDto dto)
        {
            var kullanici = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

            // Üç şart birden: hesap var + aktif + HENÜZ DOĞRULANMAMIŞ.
            // Doğrulanmış hesaba yeniden link göndermek hem anlamsız hem riskli
            // (saldırgan doğrulanmış hesapların token'ını sürekli yeniletebilirdi).
            if (kullanici != null && kullanici.IsActive && !kullanici.EmailDogrulandiMi)
            {
                var hamToken = _tokenService.RefreshTokenUret();

                // Eski token'ın hash'inin ÜZERİNE yazıyoruz → eski link ölür.
                // Aynı anda iki geçerli link dolaşmasın.
                kullanici.EmailDogrulamaTokenHash = _tokenService.Hashle(hamToken);
                kullanici.EmailDogrulamaTokenBitis = DateTime.UtcNow.AddHours(EmailTokenSaat);
                await _context.SaveChangesAsync();

                var tabanUrl = _config["Uygulama:TabanUrl"];
                var link = $"{tabanUrl}/api/auth/verify-email?token={Uri.EscapeDataString(hamToken)}";

                var govde =
                    $"<p>Merhaba {kullanici.FullName},</p>" +
                    $"<p>Hesabını doğrulamak için aşağıdaki linke tıkla (24 saat geçerli):</p>" +
                    $"<p><a href=\"{link}\">{link}</a></p>";

                await _email.GonderAsync(kullanici.Email, "Email Doğrulama (Yeniden Gönderim)", govde);
            }

            // ⭐ GÜVENLİK: forgot-password'deki mantığın aynısı — hesap olsa da
            // olmasa da, doğrulanmış olsa da olmasa da AYNI cevap.
            // Farklı cevap verirsek saldırgan "bu email kayıtlı mı?" ve
            // "doğrulanmış mı?" bilgilerini sızdırmış oluruz.
            return Ok(new { mesaj = "Eğer bu hesap doğrulanmayı bekliyorsa, yeni bir link gönderildi." });
        }



        // POST /api/auth/forgot-password  { "email": "..." }
        // Sıfırlama linki üretir ve (dev göndericiyle) gönderir.
        [EnableRateLimiting("eposta")] // ⭐ YENİ — mail bombardımanına karşı
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] SifreSifirlamaIstekDto dto)
        {
            var kullanici = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

            // ⭐ GÜVENLİK: kullanıcı bulunsa da bulunmasa da AYNI cevabı ver.
            // Neden? Farklı cevap verirsek saldırgan "bu email kayıtlı mı?" diye
            // sistemi tarayabilir (buna "user enumeration" denir). Hep aynı mesaj
            // = hangi emaillerin kayıtlı olduğunu sızdırmayız.
            if (kullanici != null && kullanici.IsActive)
            {
                var hamToken = _tokenService.RefreshTokenUret();

                kullanici.SifreSifirlamaTokenHash = _tokenService.Hashle(hamToken);
                kullanici.SifreSifirlamaTokenBitis = DateTime.UtcNow.AddHours(SifirlamaSaat);
                await _context.SaveChangesAsync();

                // ⭐ DEĞİŞTİ — TabanUrl (backend) değil, PanelUrl (React panel).
                // Sebep: /sifre-yenile bir React route'u; backend'de o adres yok,
                // TabanUrl kullanırsak kullanıcı linke tıklayınca 404 görür.
                var panelUrl = _config["Uygulama:PanelUrl"];
                var link = $"{panelUrl}/sifre-yenile?token={Uri.EscapeDataString(hamToken)}";

                var govde =
                    $"<p>Merhaba {kullanici.FullName},</p>" +
                    $"<p>Şifreni sıfırlamak için aşağıdaki linke tıkla (1 saat geçerli):</p>" +
                    $"<p><a href=\"{link}\">{link}</a></p>" +
                    $"<p>Bu isteği sen yapmadıysan bu maili görmezden gelebilirsin.</p>";

                await _email.GonderAsync(kullanici.Email, "Şifre Sıfırlama", govde);
            }

            // Her durumda aynı cevap
            return Ok(new { mesaj = "Eğer bu email kayıtlıysa, sıfırlama linki gönderildi." });
        }

        // POST /api/auth/reset-password  { "token": "...", "yeniSifre": "..." }
        // Maildeki token ile yeni şifreyi belirler.
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] SifreYenileDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.YeniSifre))
                return BadRequest(new { mesaj = "Token ve yeni şifre gerekli." });

            if (dto.YeniSifre.Length < 6)
                return BadRequest(new { mesaj = "Şifre en az 6 karakter olmalı biladerim!" });

            var hash = _tokenService.Hashle(dto.Token);
            var kullanici = await _context.Users
                .FirstOrDefaultAsync(u => u.SifreSifirlamaTokenHash == hash);

            if (kullanici == null)
                return BadRequest(new { mesaj = "Sıfırlama linki geçersiz." });

            if (kullanici.SifreSifirlamaTokenBitis == null ||
                kullanici.SifreSifirlamaTokenBitis < DateTime.UtcNow)
                return BadRequest(new { mesaj = "Sıfırlama linkinin süresi dolmuş. Lütfen tekrar iste." });

            // ✅ Yeni şifreyi hash'le ve kaydet, token'ı temizle (tek kullanımlık).
            kullanici.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.YeniSifre);
            kullanici.SifreSifirlamaTokenHash = null;
            kullanici.SifreSifirlamaTokenBitis = null;

            // ⭐ ÖNEMLİ — GÜVENLİK DAMGASINI YENİLE.
            // Şifre değişince eski oturumlar/refresh token'lar düşmeli. Damgayı
            // yenileyince eski access token'lardaki "stamp" tutmaz; ayrıca
            // aşağıda tüm refresh token'ları da iptal ediyoruz.
            kullanici.SecurityStamp = Guid.NewGuid().ToString();

            // Bu kullanıcının tüm aktif refresh token'larını iptal et (her cihazdan çıkış).
            await KullanicininTumTokenleriniIptalEt(kullanici.Id);

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Şifren güncellendi, artık yeni şifrenle giriş yapabilirsin biladerim!" });
        }



        // POST /api/auth/login
        [EnableRateLimiting("giris")] // 3a'dan: IP başına dakikada 5 deneme
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var kullanici = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

            // Kullanıcı yoksa sayaç tutamayız (satır yok) → genel mesaj.
            // (Hesabın var olup olmadığını sızdırmamak için hep aynı cümle.)
            if (kullanici == null)
                return Unauthorized(new { mesaj = "Email veya şifre hatalı biladerim!" });

            // ⭐ KİLİT KONTROLÜ — şifreyi denemeden ÖNCE bak.
            // Kilitliyse doğru şifre bile içeri almaz; süre dolunca kendiliğinden açılır.
            if (kullanici.KilitBitis != null && kullanici.KilitBitis > DateTime.UtcNow)
            {
                var kalan = (int)Math.Ceiling((kullanici.KilitBitis.Value - DateTime.UtcNow).TotalMinutes);
                return Unauthorized(new
                {
                    mesaj = $"Çok fazla hatalı deneme. Hesabın geçici kilitli, {kalan} dk sonra tekrar dene."
                });
            }

            // ⭐ ŞİFRE YANLIŞ → sayacı artır, sınırı geçtiyse kilitle.
            if (!BCrypt.Net.BCrypt.Verify(dto.Password, kullanici.PasswordHash))
            {
                kullanici.YanlisGirisSayisi++;

                if (kullanici.YanlisGirisSayisi >= MaxYanlisDeneme)
                {
                    kullanici.KilitBitis = DateTime.UtcNow.AddMinutes(KilitDakika);
                    kullanici.YanlisGirisSayisi = 0; // kilit sonrası temiz sayfa açalım
                    await _context.SaveChangesAsync();

                    return Unauthorized(new
                    {
                        mesaj = $"Çok fazla hatalı deneme. Hesabın {KilitDakika} dk kilitlendi."
                    });
                }

                await _context.SaveChangesAsync();
                return Unauthorized(new { mesaj = "Email veya şifre hatalı biladerim!" });
            }

            // ⭐ ŞİFRE DOĞRU → sayaç/kilit varsa temizle (yalnızca gerekiyorsa DB'ye yaz).
            if (kullanici.YanlisGirisSayisi != 0 || kullanici.KilitBitis != null)
            {
                kullanici.YanlisGirisSayisi = 0;
                kullanici.KilitBitis = null;
                await _context.SaveChangesAsync();
            }


            // ⭐ YENİ — email doğrulanmamışsa giriş yok.
            // "kod" alanı MAKİNE için: istemci bu durumu metne bakmadan ayırt etsin.
            // Mesaj metni ileride değişse bile kod sabit kalır, istemci kırılmaz.
            if (!kullanici.EmailDogrulandiMi)
                return Unauthorized(new
                {
                    mesaj = "Önce email adresini doğrulaman gerekiyor. Kutunu (ve konsolu) kontrol et.",
                    kod = "EMAIL_DOGRULANMADI"
                });


            if (!kullanici.IsActive)
                return Unauthorized(new { mesaj = "Hesabın devre dışı bırakılmış. Lütfen yönetici ile iletişime geç." });

            // Access (15 dk) + refresh (30 gün) üret
            var accessToken = _tokenService.TokenUret(kullanici);
            var refreshToken = await RefreshUretVeKaydet(kullanici.Id);

            return Ok(new
            {
                token = accessToken,
                refreshToken = refreshToken,
                id = kullanici.Id,
                fullName = kullanici.FullName,
                role = kullanici.Role
            });
        }

        // POST /api/auth/refresh — access 15 dk sonra ölünce istemci buraya gelir.
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.RefreshToken))
                return Unauthorized(new { mesaj = "Refresh token gerekli." });

            // Kullanıcı HAM token gönderir; biz hash'leyip DB'de onu ararız.
            var hash = _tokenService.Hashle(dto.RefreshToken);
            var kayit = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);

            if (kayit == null)
                return Unauthorized(new { mesaj = "Oturum geçersiz. Lütfen tekrar giriş yap." });

            // ⭐ HIRSIZLIK YAKALAMA: iptal edilmiş bir token yine kullanılıyorsa
            // büyük ihtimalle çalınmış → o kullanıcının TÜM token'larını iptal et.
            if (kayit.RevokedAt != null)
            {
                await KullanicininTumTokenleriniIptalEt(kayit.UserId);
                return Unauthorized(new { mesaj = "Oturum güvenliği ihlali. Lütfen tekrar giriş yap." });
            }

            if (!kayit.Aktif) // süresi dolmuş
                return Unauthorized(new { mesaj = "Oturumun süresi doldu. Lütfen tekrar giriş yap." });

            // Kullanıcının GÜNCEL hâlini oku — yeni access'i buna göre üreteceğiz,
            // böylece rol değişikliği/pasifleşme refresh anında otomatik yansır.
            var kullanici = await _context.Users.FindAsync(kayit.UserId);
            if (kullanici == null || !kullanici.IsActive)
            {
                kayit.RevokedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return Unauthorized(new { mesaj = "Hesap erişilemez durumda. Lütfen tekrar giriş yap." });
            }

            // ROTATION: eskisini iptal et, yeni refresh + yeni access ver.
            kayit.RevokedAt = DateTime.UtcNow;
            var yeniRefresh = await RefreshUretVeKaydet(kullanici.Id);
            var yeniAccess = _tokenService.TokenUret(kullanici);

            return Ok(new { token = yeniAccess, refreshToken = yeniRefresh });
        }

        // POST /api/auth/logout — verilen refresh'i iptal eder (bu cihazı çıkışa atar).
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshRequestDto dto)
        {
            if (!string.IsNullOrWhiteSpace(dto.RefreshToken))
            {
                var hash = _tokenService.Hashle(dto.RefreshToken);
                var kayit = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);

                if (kayit != null && kayit.RevokedAt == null)
                {
                    kayit.RevokedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }

            // Bulunsun bulunmasın aynı cevap — token'ın varlığını sızdırma.
            return Ok(new { mesaj = "Çıkış yapıldı." });
        }

        // GET /api/auth/ben-kimim — giriş yapan kullanıcının profili
        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpGet("ben-kimim")]
        public async Task<IActionResult> BenKimim()
        {
            var userId = int.Parse(
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

            var kullanici = await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => new
                {
                    id = u.Id,
                    fullName = u.FullName,
                    email = u.Email,
                    role = u.Role,
                    createdAt = u.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (kullanici == null)
                return NotFound(new { mesaj = "Kullanıcı bulunamadı!" });

            return Ok(kullanici);
        }

        // ---------- YARDIMCILAR ----------

        // Yeni refresh üretir, hash'ini DB'ye yazar, HAM hâlini döndürür (istemciye o gider).


        // Tarayıcıda açılan doğrulama linki için basit bir HTML sayfası üretir.
        //
        // Neden ayrı metot? Dört farklı sonuç (geçersiz / süresi dolmuş /
        // zaten doğrulanmış / başarılı) aynı sayfayı kullanıyor. HTML'i tek
        // yerde tutuyoruz — tasarım değişince tek yer düzenlenir.
        //
        // Neden panele link koymuyoruz? verify-email pratikte bir MÜŞTERİ
        // akışı (adminler kendileri kayıt olmuyor, superadmin terfi ettiriyor).
        // Müşterinin gideceği yer mobil uygulama, admin paneli değil.
        private ContentResult DogrulamaSayfasi(string baslik, string aciklama, bool basarili)
        {
            var renk = basarili ? "#27ae60" : "#e74c3c";

            var html = $@"<!DOCTYPE html>
                <html lang=""tr"">
                <head>
                  <meta charset=""utf-8"" />
                  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
                  <title>{baslik}</title>
                </head>
                <body style=""margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#f5f6fa;font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif"">
                  <div style=""background:#fff;border-radius:12px;padding:40px 32px;max-width:420px;text-align:center;box-shadow:0 4px 20px rgba(0,0,0,.08)"">
                    <h1 style=""margin:0 0 14px;font-size:22px;color:{renk}"">{baslik}</h1>
                    <p style=""margin:0;font-size:15px;line-height:1.6;color:#555"">{aciklama}</p>
                  </div>
                </body>
                </html>";

            var sonuc = Content(html, "text/html; charset=utf-8");

            // Durum kodunu da doğru ver. Tarayıcı kullanıcısı görmez ama
            // doğru HTTP semantiği önemli: hata = 400, başarı = 200.
            sonuc.StatusCode = basarili ? 200 : 400;

            return sonuc;
        }


        private async Task<string> RefreshUretVeKaydet(int userId)
        {
            var hamToken = _tokenService.RefreshTokenUret();

            var cihaz = Request.Headers["User-Agent"].ToString();
            if (cihaz.Length > 300) cihaz = cihaz.Substring(0, 300); // kolon sınırı 300

            _context.RefreshTokens.Add(new RefreshToken
            {
                UserId = userId,
                TokenHash = _tokenService.Hashle(hamToken),
                ExpiresAt = DateTime.UtcNow.AddDays(RefreshGunSayisi),
                CihazBilgisi = cihaz
            });

            await _context.SaveChangesAsync();
            return hamToken;
        }

        // Bir kullanıcının tüm aktif refresh'lerini iptal eder (hırsızlık / hepsinden çıkış).
        private async Task KullanicininTumTokenleriniIptalEt(int userId)
        {
            var aktifler = await _context.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ToListAsync();

            foreach (var t in aktifler)
                t.RevokedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }
    }
}