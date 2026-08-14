using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;


using Microsoft.AspNetCore.RateLimiting; // ⭐ YENİ

// ⭐ YENİ — GuvenliGonderAsync bir UZANTI METODU ve uzantı metotları
// tam nitelikli adla (ETicaretAPI.Services.…) çağrılamaz; namespace'in
// using ile içeri alınması ŞART. Bu dosyada tipler tam nitelikli
// yazıldığı için using yoktu ve derleyici "böyle bir tanım yok" diyordu.
using ETicaretAPI.Services;

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


        // ⭐ YENİ — Token'daki kullanıcı kimliğini okur.
        //
        // Neden istekten değil token'dan?
        //   Token JWT ve sunucunun gizli anahtarıyla imzalı — içeriği
        //   değiştirilemez. İstek gövdesindeki bir "userId" ise istemcinin
        //   yazdığı düz metindir; biri başkasının id'sini yazıp onun
        //   şifresini değiştirmeyi denerdi.
        //
        // "!" işareti: [Authorize] geçildiyse bu claim kesin vardır.
        // Yoksa endpoint'e hiç girilemezdi.
        private int GetUserId()
        {
            return int.Parse(
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        }


        private readonly ETicaretAPI.Services.IEmailGonderici _email;
        private readonly IConfiguration _config;

        // ⭐ YENİ (Aşama 10) — sözleşme onaylarını yazan ortak servis
        private readonly ETicaretAPI.Services.SozlesmeOnayServisi _onayServisi;

        // ⭐ YENİ — hata kaydı için.
        //
        // Neden gerekti? HesabimiSil'deki catch bloğu istisnayı
        // yakalayıp kendi cevabını döndürüyor, yani global
        // HataYakalamaMiddleware'e HİÇ ulaşmıyor. Middleware log'lamayı
        // orada yaptığı için, o dal boyunca hata hiçbir yere
        // yazılmıyordu. Yakalayan, log'lamaktan da sorumludur.
        private readonly ILogger<AuthController> _log;

        // ⭐ YENİ — profil fotoğrafı wwwroot'a yazılıyor; klasörün
        // diskteki yerini bu biliyor. (ProductsController ve
        // AdminKampanyalarController ile aynı gerekçe.)
        private readonly IWebHostEnvironment _env;

        // ⭐ YENİ — giriş denemelerinin kalıcı kaydı.
        // ⚠️ Kendi kapsamında yazıyor: log yazma hatası girişi bozmasın.
        private readonly ETicaretAPI.Services.SistemGunlugu _gunluk;

        public AuthController(
            AppDbContext context,
            ETicaretAPI.Services.TokenService tokenService,
            ETicaretAPI.Services.IEmailGonderici email,   // ⭐ YENİ
            IConfiguration config,                        // ⭐ YENİ
            ETicaretAPI.Services.SozlesmeOnayServisi onayServisi,   // ⭐ YENİ (Aşama 10)
            ILogger<AuthController> log,                  // ⭐ YENİ
            IWebHostEnvironment env,                      // ⭐ YENİ
            ETicaretAPI.Services.SistemGunlugu gunluk)    // ⭐ YENİ
        {
            _context = context;
            _tokenService = tokenService;
            _email = email;     // ⭐ YENİ
            _config = config;   // ⭐ YENİ
            _onayServisi = onayServisi;
            _log = log;         // ⭐ YENİ
            _env = env;         // ⭐ YENİ
            _gunluk = gunluk;   // ⭐ YENİ
        }

        // POST /api/auth/register
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            // ⭐ YENİ (Aşama 10) — sözleşme onayı olmadan kayıt yok.
            if (!dto.SozlesmeOnayi)
            {
                return BadRequest(new
                {
                    mesaj = "Kayıt için gizlilik politikası ve kullanım koşullarını onaylaman gerekiyor."
                });
            }

            // ⭐ YENİ — E-POSTAYI NORMALLEŞTİR
            //
            // Neden? "  Ali@Mail.COM  " ile "ali@mail.com" aynı adrestir.
            // Kullanıcı mobil klavyede baş harfi büyük yazabilir, kopyala-
            // yapıştırda başa boşluk gelebilir.
            //
            // SQL Server'ın varsayılan harmanlaması harf duyarsız olduğu için
            // unique index bunları zaten çakıştırırdı. Ama veriyi normalize
            // ETMEK yine de doğru: veritabanında tek bir kanonik biçim durur,
            // e-posta gönderirken/karşılaştırırken sürpriz olmaz ve harmanlama
            // ileride değişse bile davranış bozulmaz.
            var temizEmail = dto.Email.Trim().ToLowerInvariant();

            // ⚠️ Bu kontrol yarış koşuluna AÇIK (TOCTOU): kontrol ile INSERT
            // arasında başka bir istek araya girebilir. Asıl koruma aşağıdaki
            // catch bloğundaki UNIQUE INDEX'tir.
            //
            // Peki bu kontrol niye duruyor? Çünkü %99 durumda hata buradan
            // yakalanır ve kullanıcı net bir mesaj alır. Exception yolu
            // hem daha pahalıdır hem de sadece nadir yarış durumu içindir.
            var emailVarMi = await _context.Users.AnyAsync(u => u.Email == temizEmail);
            if (emailVarMi)
                return BadRequest(new { mesaj = "Bu email zaten kayıtlı biladerim!" });

            var hashlenmisSifre = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            // ⭐ Doğrulama token'ı üret. RefreshTokenUret zaten güvenli-rastgele
            // opaque bir metin üretiyor; aynısını burada da kullanıyoruz.
            var hamToken = _tokenService.RefreshTokenUret();

            var yeniKullanici = new User
            {
                FullName = dto.FullName.Trim(),
                Email = temizEmail,                  // ⭐ normalize edilmiş hâli
                PasswordHash = hashlenmisSifre,
                Role = "customer",

                // ⭐ Doğrulanmamış başlar; linke tıklayınca açılacak
                EmailDogrulandiMi = false,
                EmailDogrulamaTokenHash = _tokenService.Hashle(hamToken), // sadece HASH saklanır
                EmailDogrulamaTokenBitis = DateTime.UtcNow.AddHours(EmailTokenSaat)
            };

            _context.Users.Add(yeniKullanici);

            // ⭐ YENİ — YARIŞ KOŞULU KALKANI
            //
            // Yukarıdaki AnyAsync kontrolünü geçmiş olsak bile, tam bu anda
            // başka bir istek aynı e-postayı kaydetmiş olabilir. O durumda
            // SQL Server unique index ihlali fırlatır ve EF bunu
            // DbUpdateException olarak sarmalar.
            //
            // Bu bizim için bir HATA değil, beklenen bir durum — kullanıcıya
            // aynı nazik mesajı veriyoruz. Yakalamasaydık global hata
            // middleware'i devreye girip 500 dönerdi ve kullanıcı
            // "sunucu hatası" görürdü.
            try
            {
                await _context.SaveChangesAsync();

                // ⭐ YENİ (Aşama 10) — onay kaydı.
                // Kullanıcı kaydedildikten SONRA: onay satırı UserId'ye
                // FK ile bağlı, kullanıcı olmadan yazılamaz.
                // ⭐ DEĞİŞTİ — IP artık ortak yardımcıdan (IstemciAdresi).
                // Vekil kuralı değişirse tek yerde değişsin diye.
                await _onayServisi.EkleAsync(
                    yeniKullanici.Id,
                    SozlesmeTipi.KayitSozlesmeleri,
                    ETicaretAPI.Support.IstemciAdresi.Oku(HttpContext));

                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Not: Burada exception'ın gerçekten unique index ihlali
                // olduğunu SQL hata numarasına (2601 / 2627) bakarak da
                // doğrulayabilirdik. Bu metotta tek unique kısıt e-posta
                // olduğu için gerek duymuyoruz; Users tablosuna ikinci bir
                // unique kısıt eklenirse burayı gözden geçirmek gerekir.
                return BadRequest(new { mesaj = "Bu email zaten kayıtlı biladerim!" });
            }

            // ⭐ Doğrulama linkini kur ve (dev göndericiyle) gönder.
            // HAM token linke gider; DB'de yalnızca hash var → link sızsa bile
            // DB'den geri üretilemez.
            var tabanUrl = _config["Uygulama:TabanUrl"];
            var link = $"{tabanUrl}/api/auth/verify-email?token={Uri.EscapeDataString(hamToken)}";

            var govde =
                $"<p>Merhaba {yeniKullanici.FullName},</p>" +
                $"<p>Hesabını doğrulamak için aşağıdaki linke tıkla (24 saat geçerli):</p>" +
                $"<p><a href=\"{link}\">{link}</a></p>";

            // ⭐⭐ DEĞİŞTİ — ARTIK GuvenliGonderAsync, ÇIPLAK GonderAsync DEĞİL.
            //
            // ⚠️ BU SATIR KONSOL GÖNDERİCİSİYLE ASLA PATLAMIYORDU, GERÇEK
            // SAĞLAYICIYLA PATLAYABİLİR (ağ, kota, geçersiz anahtar).
            //
            // Korumasız hâlinin sonucu şuydu: kullanıcı yukarıda
            // SaveChanges ile ZATEN veritabanına yazılmış oluyor. Mail
            // burada patlarsa istek 500 döner, kullanıcı "kayıt olamadım"
            // sanıp tekrar dener ve bu sefer "Bu email zaten kayıtlı"
            // alır. Ne girebildiği ne de yeniden kaydolabildiği,
            // kurtarılamaz bir hesap.
            //
            // ⚠️ Mesaj yine "linke tıkla" diyor, oysa mail gitmemiş
            // olabilir. Bilinçli: kurtarma yolu ZATEN VAR
            // (POST /api/auth/resend-verification). Akışın ortasında
            // "kaydın oldu ama mailin gitmedi" demek kullanıcıyı ne
            // yapacağını bilmez halde bırakırdı; hata günlüğe düşüyor.
            await _email.GuvenliGonderAsync(
                _log,
                yeniKullanici.Email,
                new ETicaretAPI.Services.EmailIcerik("Email Doğrulama", govde),
                "EmailDogrulama:Kayit");

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

                // ⭐ DEĞİŞTİ — GuvenliGonderAsync. Gerekçe Register'da yazılı.
                //
                // ⚠️ Burada patlaması ayrıca can sıkıcı olurdu: bu uç zaten
                // "mailim gelmedi" diyen kullanıcının son çaresi. 500
                // dönseydi kullanıcının elinde hiçbir yol kalmazdı.
                await _email.GuvenliGonderAsync(
                    _log,
                    kullanici.Email,
                    new ETicaretAPI.Services.EmailIcerik("Email Doğrulama (Yeniden Gönderim)", govde),
                    "EmailDogrulama:YenidenGonderim");
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

                // ⭐ DEĞİŞTİ — GuvenliGonderAsync. Gerekçe Register'da yazılı.
                //
                // ⚠️ Burada yutmak ayrıca ZORUNLU: bu uç hesap sızdırmamak
                // için her durumda aynı cevabı dönüyor. Mail hatası 500'e
                // dönüşseydi, "500 aldım demek ki bu e-posta kayıtlı"
                // çıkarımı yapılabilirdi — aşağıdaki genel mesajın koruduğu
                // bilgi tam da böyle sızardı.
                await _email.GuvenliGonderAsync(
                    _log,
                    kullanici.Email,
                    new ETicaretAPI.Services.EmailIcerik("Şifre Sıfırlama", govde),
                    "SifreSifirlama");
            }

            // Her durumda aynı cevap
            return Ok(new { mesaj = "Eğer bu email kayıtlıysa, sıfırlama linki gönderildi." });
        }

        // POST /api/auth/reset-password  { "token": "...", "yeniSifre": "..." }
        // Maildeki token ile yeni şifreyi belirler.
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] SifreYenileDto dto)
        {
            // ⭐ DEĞİŞTİ — elle yazılmış iki kontrol kaldırıldı.
            //
            // Token varlığı ve şifre uzunluğu artık SifreYenileDto'daki
            // özniteliklerde ([Required] + [SifreGuclu]). [ApiController]
            // onları otomatik yakalıyor ve diğer tüm doğrulama
            // hatalarıyla AYNI zarfla döndürüyor.
            //
            // Eskiden uzunluk kuralı burada ve SifreDegistirDto'da ayrı
            // ayrı yazılıydı; kayıtta ise hiç yoktu. Üç kopya tek
            // kaynağa toplandı.
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
            // ⭐ YENİ — GİRİŞ KAYDI.
            //
            // Bugüne kadar yalnızca bir SAYAÇ vardı (YanlisGirisSayisi) ve
            // o sayaç başarılı girişte sıfırlanıyor: "bu hesaba dün gece
            // 40 kez denendi" sorusu cevaplanamıyordu.
            //
            // ⚠️ Şifre HİÇBİR dalda yazılmıyor — yanlış girilen bile.
            // Yanlış şifre çoğu zaman kullanıcının BAŞKA bir hesaptaki
            // doğru şifresidir.
            var ip = ETicaretAPI.Support.IstemciAdresi.Oku(HttpContext);

            var kullanici = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

            // Kullanıcı yoksa sayaç tutamayız (satır yok) → genel mesaj.
            // (Hesabın var olup olmadığını sızdırmamak için hep aynı cümle.)
            if (kullanici == null)
            {
                // ⚠️ Kayıtta "kullanici_yok" yazıyor ama CEVAP değişmiyor:
                // hesabın varlığı yalnızca süperadmine açık bu tabloda
                // görünüyor, istemciye sızmıyor.
                await _gunluk.GirisYazAsync(dto.Email, GirisSonucu.KullaniciYok, ip);

                return Unauthorized(new { mesaj = "Email veya şifre hatalı biladerim!" });
            }

            // ⭐ KİLİT KONTROLÜ — şifreyi denemeden ÖNCE bak.
            // Kilitliyse doğru şifre bile içeri almaz; süre dolunca kendiliğinden açılır.
            if (kullanici.KilitBitis != null && kullanici.KilitBitis > DateTime.UtcNow)
            {
                var kalan = (int)Math.Ceiling((kullanici.KilitBitis.Value - DateTime.UtcNow).TotalMinutes);

                await _gunluk.GirisYazAsync(dto.Email, GirisSonucu.HesapKilitli, ip);

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

                    await _gunluk.GirisYazAsync(dto.Email, GirisSonucu.SifreYanlis, ip);

                    return Unauthorized(new
                    {
                        mesaj = $"Çok fazla hatalı deneme. Hesabın {KilitDakika} dk kilitlendi."
                    });
                }

                await _context.SaveChangesAsync();

                await _gunluk.GirisYazAsync(dto.Email, GirisSonucu.SifreYanlis, ip);

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
            {
                await _gunluk.GirisYazAsync(dto.Email, GirisSonucu.Dogrulanmamis, ip);

                return Unauthorized(new
                {
                    mesaj = "Önce email adresini doğrulaman gerekiyor. Kutunu (ve konsolu) kontrol et.",
                    kod = "EMAIL_DOGRULANMADI"
                });
            }


            if (!kullanici.IsActive)
            {
                await _gunluk.GirisYazAsync(dto.Email, GirisSonucu.HesapPasif, ip);

                return Unauthorized(new { mesaj = "Hesabın devre dışı bırakılmış. Lütfen yönetici ile iletişime geç." });
            }

            // Access (15 dk) + refresh (30 gün) üret
            var accessToken = _tokenService.TokenUret(kullanici);
            var refreshToken = await RefreshUretVeKaydet(kullanici.Id);

            // ⚠️ BAŞARILI GİRİŞ DE KAYDEDİLİYOR, yalnızca başarısızlar
            // değil. "Bu hesaba nereden girildi" sorusu, hesap ele
            // geçirildiğinde sorulan ilk soru; yalnızca başarısızları
            // tutan bir tablo onu cevaplayamaz.
            await _gunluk.GirisYazAsync(dto.Email, GirisSonucu.Basarili, ip);

            return Ok(new
            {
                token = accessToken,
                refreshToken = refreshToken,
                id = kullanici.Id,
                fullName = kullanici.FullName,
                role = kullanici.Role,

                // ⭐ YENİ — girişte de dönüyor ki uygulama açılır
                // açılmaz avatar doğru çizilsin. Yalnızca profil
                // ucundan dönseydi, giriş yapan müşteri fotoğrafını
                // ancak Hesabım'a girip çıktıktan sonra görürdü.
                profilFotoUrl = kullanici.ProfilFotoUrl
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
                    createdAt = u.CreatedAt,
                    profilFotoUrl = u.ProfilFotoUrl   // ⭐ YENİ
                })
                .FirstOrDefaultAsync();

            if (kullanici == null)
                return NotFound(new { mesaj = "Kullanıcı bulunamadı!" });

            return Ok(kullanici);
        }


        // ⭐ YENİ — POST /api/auth/change-password
        //
        // Giriş yapmış kullanıcının kendi şifresini değiştirmesi.
        //
        // "Şifremi unuttum" akışından farkı:
        //   forgot/reset  → maille gelen tek kullanımlık token ile, giriş
        //                   yapmadan. Şifreyi HATIRLAMAYAN kullanıcı için.
        //   change        → oturum + eski şifre ile. Şifreyi bilen ama
        //                   değiştirmek isteyen kullanıcı için.
        //
        // Rate limit neden var? Saldırgan bir şekilde access token ele
        // geçirdiyse, eski şifreyi deneme yanılma ile bulmaya çalışabilir.
        // Dakikada 5 deneme bunu pratikte imkânsız kılar.
        [Microsoft.AspNetCore.Authorization.Authorize]
        [EnableRateLimiting("giris")]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] SifreDegistirDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = GetUserId();

            var kullanici = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (kullanici == null || !kullanici.IsActive)
                return Unauthorized(new { mesaj = "Oturum geçersiz. Lütfen tekrar giriş yap." });

            // ---------- 1) ESKİ ŞİFREYİ DOĞRULA ----------
            //
            // BCrypt.Verify hash'i geri çözmez — girilen şifreyi aynı tuzla
            // (salt) yeniden hash'leyip sonucu karşılaştırır. Hash tek yönlüdür.
            var eskiDogruMu = BCrypt.Net.BCrypt.Verify(dto.EskiSifre, kullanici.PasswordHash);

            if (!eskiDogruMu)
            {
                // Mesajda "eski şifre yanlış" demek sorun değil — kullanıcı
                // zaten kendi hesabında. Login'deki "gizleme" mantığı burada
                // geçerli değil, orada hesabın VARLIĞINI sızdırmamak içindi.
                return BadRequest(new { mesaj = "Mevcut şifren yanlış." });
            }

            // ---------- 2) YENİ ŞİFRE ESKİSİYLE AYNI OLMASIN ----------
            //
            // Kullanıcı yanlışlıkla aynı şifreyi yazdıysa uyaralım. Aksi halde
            // "şifren değişti" der, hiçbir şey değişmemiş olur ve üstelik
            // diğer cihazlardaki oturumları boşuna düşürmüş oluruz.
            if (BCrypt.Net.BCrypt.Verify(dto.YeniSifre, kullanici.PasswordHash))
            {
                return BadRequest(new
                {
                    mesaj = "Yeni şifre eskisiyle aynı olamaz."
                });
            }

            // ---------- 3) YENİ ŞİFREYİ KAYDET ----------
            kullanici.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.YeniSifre);

            // ---------- 4) GÜVENLİK DAMGASINI YENİLE ----------
            //
            // Damga JWT'nin içinde "stamp" claim'i olarak taşınıyor.
            // GuvenlikDamgasiMiddleware her istekte token'daki damgayı
            // veritabanındakiyle karşılaştırıyor.
            //
            // ⚠️ BU CÜMLE MIDDLEWARE PIPELINE'DA AÇIK OLDUĞU SÜRECE
            // DOĞRU (Program.cs). Middleware bir kez kapatıldı ve o
            // dönemde buradaki "anında geçersizleşir" iddiası sessizce
            // yalan oldu — damga yenileniyor ama kimse bakmıyordu.
            //
            // Damgayı değiştirince ELDEKİ TÜM access token'lar anında
            // geçersizleşir — 15 dakika beklemeye gerek kalmaz.
            //
            // reset-password endpoint'i de aynısını yapıyor: tutarlılık.
            kullanici.SecurityStamp = Guid.NewGuid().ToString();

            // ---------- 5) TÜM REFRESH TOKEN'LARI İPTAL ET ----------
            //
            // Access token 15 dakikalık, damga ile hemen öldü. Ama refresh
            // token 30 gün ömürlü ve damga kontrolünden geçmiyor — onları
            // ayrıca iptal etmek ZORUNDAYIZ. Yoksa saldırgan refresh ile
            // yeni access alıp devam ederdi.
            //
            // ⚠️ SIRA KRİTİK: Bu adım, aşağıda kendi yeni token'ımızı
            //    üretmeden ÖNCE olmalı. Sonra yapsaydık kendi token'ımızı
            //    da iptal etmiş olurduk.
            await KullanicininTumTokenleriniIptalEt(kullanici.Id);

            await _context.SaveChangesAsync();

            // ---------- 6) BU CİHAZ İÇİN YENİ TOKEN ÇİFTİ ----------
            //
            // KARAR: Şifreyi değiştiren kişi hem eski hem yeni şifreyi
            // biliyor — kimliğini kanıtlamış durumda. Onu çıkışa atmak
            // güvenlik kazancı sağlamaz, sadece rahatsız eder.
            //
            // Diğer cihazlar düştü (adım 4-5), bu cihaz devam ediyor.
            // Gmail, GitHub ve banka uygulamaları da böyle davranır.
            //
            // Alternatif: hiç token döndürmeyip kullanıcıyı giriş ekranına
            // atmak. Daha basit olurdu ama kötü deneyim.
            var yeniRefresh = await RefreshUretVeKaydet(kullanici.Id);
            var yeniAccess = _tokenService.TokenUret(kullanici);

            // ---------- 7) BİLGİLENDİRME MAİLİ ----------
            //
            // Şifre değişikliği bir GÜVENLİK OLAYIDIR. Kullanıcı bunu
            // yapmadıysa haberdar olmalı — hesabı ele geçirilmiş demektir.
            //
            // Mail gönderilemezse şifre değişikliği GEÇERLİ kalmalı.
            // Mail bir bildirimdir, işlemin parçası değil. Bunu
            // yapmasaydık mail sağlayıcısı çökünce kullanıcılar şifre
            // değiştiremez hale gelirdi.
            //
            // ⭐ DEĞİŞTİ — elle yazılmış `try { } catch { }` yerine
            // GuvenliGonderAsync.
            //
            // ⚠️ Eski hâli istisnayı ÇIPLAK yutuyordu: loglama bile yoktu.
            // Gönderim başarısız olduğunda bunu anlamanın hiçbir yolu
            // yoktu — üstelik bu bir GÜVENLİK bildirimi, gitmediğini
            // bilmek gerekiyor. Ortak sarmalayıcı LogError ile kaydediyor
            // ve olay adını da yazıyor.
            var sifreDegistiGovde =
                $"<p>Merhaba {kullanici.FullName},</p>" +
                "<p>Hesabının şifresi değiştirildi ve diğer tüm " +
                "cihazlardaki oturumların kapatıldı.</p>" +
                "<p>Bu işlemi sen yapmadıysan hemen \"Şifremi Unuttum\" " +
                "ile şifreni sıfırla.</p>";

            await _email.GuvenliGonderAsync(
                _log,
                kullanici.Email,
                new ETicaretAPI.Services.EmailIcerik("Şifren Değiştirildi", sifreDegistiGovde),
                "SifreDegistirildi");

            return Ok(new
            {
                mesaj = "Şifren güncellendi. Diğer cihazlardaki oturumların kapatıldı.",
                token = yeniAccess,
                refreshToken = yeniRefresh
            });
        }


        // ⭐ YENİ — PUT /api/auth/profil
        //
        // Şu an sadece ad soyad değiştirilebiliyor.
        //
        // Neden SecurityStamp yenilemiyoruz?
        //   Damga yenilemek "eldeki tüm token'ları öldür" demek. Bu ağır
        //   bir işlem ve yalnızca GÜVENLİK durumu değiştiğinde yapılır:
        //   şifre değişikliği, rol değişikliği, hesap pasifleştirme.
        //
        //   Ad soyad değişikliği güvenlik durumunu etkilemiyor. Damgayı
        //   yenilesek kullanıcı adını her düzelttiğinde tüm cihazlarından
        //   düşerdi — sebepsiz bir ceza.
        //
        // JWT'de FullName claim'i YOK (TokenService'e baktım: sadece Id,
        // Email, rol ve damga var). Yani token'ın tazelenmesi de gerekmiyor.
        // İstemci kendi sakladığı kopyayı cevaptan güncelleyecek.
        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpPut("profil")]
        public async Task<IActionResult> ProfilGuncelle([FromBody] ProfilGuncelleDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = GetUserId();

            var kullanici = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (kullanici == null || !kullanici.IsActive)
                return Unauthorized(new { mesaj = "Oturum geçersiz. Lütfen tekrar giriş yap." });

            // Trim: baştaki/sondaki boşluk temizlensin. Kayıt olurken de
            // aynısını yapıyoruz (Register'da FullName.Trim()) — tutarlılık.
            kullanici.FullName = dto.FullName.Trim();

            await _context.SaveChangesAsync();

            // Güncellenmiş profili döndürüyoruz.
            //
            // Neden sadece "başarılı" demiyoruz? Çünkü istemci ekranda
            // gösterdiği kopyayı güncellemek zorunda. Cevapta veriyi
            // dönersek istemci ayrıca bir GET isteği atmak zorunda kalmaz —
            // bir ağ turu kazanırız.
            return Ok(new
            {
                mesaj = "Profilin güncellendi biladerim!",
                id = kullanici.Id,
                fullName = kullanici.FullName,
                email = kullanici.Email,
                role = kullanici.Role,
                profilFotoUrl = kullanici.ProfilFotoUrl   // ⭐ YENİ
            });
        }


        // ⭐ YENİ — POST /api/auth/profil-foto   (multipart, alan adı: dosya)
        //
        // ⚠️ AYRI UÇ, profil güncellemeye eklenmedi. Ad değiştirme JSON,
        // fotoğraf ise multipart; ikisini tek uca sıkıştırmak, ad
        // değiştirmek isteyenin her seferinde form-data kurmasını
        // gerektirirdi.
        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpPost("profil-foto")]
        public async Task<IActionResult> ProfilFotoYukle([FromForm] IFormFile dosya)
        {
            var userId = int.Parse(
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

            var kullanici = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (kullanici == null || !kullanici.IsActive)
            {
                return Unauthorized(new { mesaj = "Oturum geçersiz. Lütfen tekrar giriş yap." });
            }

            // ⚠️ Doğrulama ürün ve banner yüklemeyle AYNI serviste:
            // boyut, uzantı, MIME ve byte kontrolü. Buraya ayrı bir
            // kural yazsaydık, birinde kapatılan bir açık burada
            // açık kalırdı.
            var hata = await ResimDosyasi.DogrulaAsync(dosya);

            if (hata != null)
            {
                return BadRequest(new { mesaj = hata });
            }

            var eski = kullanici.ProfilFotoUrl;

            kullanici.ProfilFotoUrl = await ResimDosyasi.DiskeYazAsync(_env, dosya, "profil");
            await _context.SaveChangesAsync();

            // ⚠️ Eski dosya kayıt yazıldıktan SONRA siliniyor. Ters
            // sırada olsaydı SaveChanges patladığında kayıt eski
            // adresi gösterirken dosya diskte olmazdı.
            ResimDosyasi.DiskDosyasiniSil(_env, eski);

            return Ok(new
            {
                mesaj = "Profil fotoğrafın güncellendi.",
                profilFotoUrl = kullanici.ProfilFotoUrl
            });
        }


        // ⭐ YENİ — DELETE /api/auth/profil-foto
        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpDelete("profil-foto")]
        public async Task<IActionResult> ProfilFotoSil()
        {
            var userId = int.Parse(
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

            var kullanici = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (kullanici == null || !kullanici.IsActive)
            {
                return Unauthorized(new { mesaj = "Oturum geçersiz. Lütfen tekrar giriş yap." });
            }

            var eski = kullanici.ProfilFotoUrl;

            kullanici.ProfilFotoUrl = null;
            await _context.SaveChangesAsync();

            ResimDosyasi.DiskDosyasiniSil(_env, eski);

            // ⚠️ Fotoğrafı zaten olmayan kullanıcıda da 200: istenen
            // son durum sağlanmış. 400 dönmek, iki kez basan
            // müşteriye olmayan bir hata göstermek olurdu.
            return Ok(new { mesaj = "Profil fotoğrafın kaldırıldı." });
        }


        // ⭐ YENİ — POST /api/auth/hesabimi-sil
        //
        // KVKK "unutulma hakkı" karşılığı. Ama kullanıcı satırı GERÇEKTEN
        // SİLİNMİYOR — anonimleştiriliyor.
        //
        // Neden gerçekten silmiyoruz?
        //   1. Review.UserId üzerindeki FK "Restrict" ayarlı — veritabanı
        //      silmeyi zaten reddeder
        //   2. Siparişler yetim kalır, ciro raporları bozulur
        //   3. Sipariş bir muhasebe kaydıdır, silinmesi yasal sorun yaratır
        //
        // Çözüm: ticari kayıt kalır, kişisel veri gider.
        //
        // Neden POST, DELETE değil?
        //   a) Şifre onayı gövdede taşınıyor; DELETE gövdesi HTTP'de
        //      tanımsız ve bazı ara katmanlar sessizce atıyor
        //   b) Yaptığımız iş kaynak silme değil, durum geçişi
        //
        // Rate limit: şifre deneme yanılmasını engellemek için.
        [Microsoft.AspNetCore.Authorization.Authorize]
        [EnableRateLimiting("giris")]
        [HttpPost("hesabimi-sil")]
        public async Task<IActionResult> HesabimiSil([FromBody] HesapSilDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = GetUserId();

            var kullanici = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (kullanici == null || !kullanici.IsActive)
                return Unauthorized(new { mesaj = "Oturum geçersiz. Lütfen tekrar giriş yap." });

            // ---------- 1) ŞİFRE ONAYI ----------
            if (!BCrypt.Net.BCrypt.Verify(dto.Sifre, kullanici.PasswordHash))
            {
                return BadRequest(new { mesaj = "Şifren yanlış. Hesabın kapatılmadı." });
            }

            // ---------- 2) SÜPERADMİN KORUMASI ----------
            //
            // Süperadmin kendini kapatırsa sistemi yönetecek kimse kalmaz:
            // rol atayamaz, admin başvurusu onaylayamaz, kimseyi
            // yetkilendiremez. Kilidi içeride bırakıp kapıyı kapatmak gibi.
            //
            // Normal admin kendini kapatabilir — denetim kayıtlarındaki
            // ActorName dondurulmuş olduğu için geçmiş işlemleri izlenebilir
            // kalır ve başka adminler sistemi yönetmeye devam eder.
            if (kullanici.Role == "superadmin")
            {
                return BadRequest(new
                {
                    mesaj = "Süperadmin hesabı bu ekrandan kapatılamaz. " +
                            "Önce başka bir kullanıcıya süperadmin yetkisi verilmeli."
                });
            }

            // ---------- 3) BİLGİLENDİRME İÇİN ESKİ BİLGİLERİ SAKLA ----------
            //
            // ⚠️ Maili ANONİMLEŞTİRMEDEN ÖNCE yakalamak zorundayız.
            //    Sonra yakalamaya kalksak "silinmis_47@silinmis.local"
            //    adresine mail göndermeye çalışırdık.
            var eskiEmail = kullanici.Email;
            var eskiAd = kullanici.FullName;

            // ---------- 4) TRANSACTION ----------
            //
            // Neden şart? Altı ayrı yazma işlemi var. Üçüncüsünde uygulama
            // çökse: adresler ve kartlar silinmiş, kullanıcı hâlâ aktif,
            // oturumlar açık. Yarı silinmiş bir hesap — en kötü durum.
            //
            // Transaction "ya hepsi ya hiçbiri" garantisi veriyor.
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // ---------- 5) KİŞİSEL VERİLERİ SİL ----------
                //
                // ExecuteDeleteAsync: tek SQL cümlesiyle siler.
                //   Klasik yol: SELECT ile hepsini belleğe çek → RemoveRange →
                //               SaveChanges → EF her satır için ayrı DELETE
                //   Bu yol:     DELETE FROM Addresses WHERE UserId = @id
                //
                // 50 adresi olan bir kullanıcıda 51 sorgu yerine 1 sorgu.
                // Ayrıca change tracker'a hiç yüklenmiyor.
                //
                // Mevcut transaction'a otomatik dahil olur — rollback bunları
                // da geri alır.
                await _context.Addresses
                    .Where(a => a.UserId == userId)
                    .ExecuteDeleteAsync();

                await _context.Cards
                    .Where(c => c.UserId == userId)
                    .ExecuteDeleteAsync();

                await _context.CartItems
                    .Where(c => c.UserId == userId)
                    .ExecuteDeleteAsync();

                await _context.Favorites
                    .Where(f => f.UserId == userId)
                    .ExecuteDeleteAsync();

                // ⭐ YENİ (Aşama 10) — telefon defteri de kişisel veri.
                // ⚠️ Adres silme BUNDAN ÖNCE olmalı: adresler telefona
                // FK ile bağlı (SET NULL olsa da sıra tutarlı kalsın).
                await _context.Phones
                    .Where(p => p.UserId == userId)
                    .ExecuteDeleteAsync();

                // ⭐ YENİ — kaydedilmiş ("hızlı") siparişler.
                //
                // ⚠️ Siparişin KENDİSİ silinmiyor — o ticari kayıt ve
                // aşağıda anonimleştirmeyle korunuyor. Burada silinen
                // yalnızca "müşteri bunu kaydetmişti" işareti; bu bir
                // kişisel tercih, muhasebe kaydı değil.
                // Favoriler ve sepetle aynı muamele.
                await _context.HizliSiparisler
                    .Where(h => h.UserId == userId)
                    .ExecuteDeleteAsync();

                // ---------- 6) OTURUMLARI KAPAT ----------
                //
                // Burada iptal etmek (RevokedAt) yerine SİLİYORUZ.
                //
                // Normalde iptal edilen token'ları saklıyoruz — hırsızlık
                // tespiti için gerekli (iptal edilmiş token tekrar kullanılırsa
                // alarm veriyoruz). Ama hesap kapatıldıysa o kullanıcı için
                // hırsızlık tespitinin bir anlamı kalmıyor: token'ın sahibi
                // ne yaparsa yapsın giriş yapamaz.
                //
                // Ayrıca token kayıtları cihaz bilgisi (CihazBilgisi) taşıyor —
                // bu da kişisel veri. KVKK gereği gitmeli.
                await _context.RefreshTokens
                    .Where(t => t.UserId == userId)
                    .ExecuteDeleteAsync();

                // ---------- 7) DENETİM KAYDINDAKİ ADI MASKELE ----------
                //
                // ⚠️ ⭐ YENİ — BU ADIM EKSİKTİ VE SESSİZ BİR KVKK AÇIĞIYDI.
                //
                // AuditLog.ActorName ve TargetName DONMUŞ KOPYALAR:
                // aşağıdaki anonimleştirme onlara işlemiyor ve kişisel
                // veri denetim tablosunda kalmaya devam ediyordu.
                //
                // ⚠️ İKİ ALAN DA maskeleniyor. Yalnızca birini yapmak işe
                // yaramazdı: müşteri genelde TargetName, admin ise
                // ActorName tarafında duruyor ve hangisi olduğu hesabı
                // kapatan kişiye göre değişiyor.
                //
                // ⚠️ AYNI TRANSACTION'DA. Ayrı olsaydı hesap kapanır ama
                // maskeleme patlarsa kişisel veri tabloda kalırdı.
                //
                // ⚠️ YAZARKEN maskeleniyor, okurken değil: veri kalıcı
                // olarak gidiyor. "Gizlenmiş ama duran" veri KVKK'nın
                // silme hakkını karşılamıyor. Bedeli bilinçli kabul
                // edildi — adli bir talepte "bu işlemi kim yaptı" sorusu
                // isimle cevaplanamayacak.
                //
                // ⚠️ ActorUserId MASKELENMİYOR. Ad kişisel veri, kimlik
                // numarası ise iç referans; ikisini birden silmek kaydı
                // anlamsız bırakırdı ("birileri fiyatı değiştirdi").
                // Kullanıcı satırı zaten anonimleştiği için o id bir
                // kişiye geri götürmüyor.
                var maskeliAd = ETicaretAPI.Support.Maskeleme.Ad(kullanici.FullName);

                // ActorName HER ZAMAN bir kişi adı: DenetimKaydi onu
                // Users.FullName'den okuyor. Koşulsuz maskeleniyor.
                await _context.AuditLogs
                    .Where(l => l.ActorUserId == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.ActorName, maskeliAd));

                // ⚠️⚠️ TargetName HER ZAMAN KİŞİ ADI DEĞİL.
                //
                // Bazı kayıtlarda VARLIK ETİKETİ taşıyor:
                // "Ürün: Kahve (#42)", "Sipariş SP-260814-1", "Kupon: YAZ25".
                // Hepsini maskeleseydik o etiketler "E***" olur ve
                // denetim kaydı hangi ürüne/siparişe ait olduğunu
                // kaybederdi — kişisel veriyi silerken TİCARİ bilgiyi de
                // silmek olurdu.
                //
                // Ayrım ":" karakteri: varlık etiketlerinin tamamı
                // DenetimEtiketi üzerinden üretiliyor ve hepsi
                // "Tür: değer" biçiminde. Kişi adında iki nokta olmaz.
                //
                // ⚠️ BU YÜZDEN YENİ BİR VARLIK ETİKETİ ELLE YAZILMAMALI —
                // DenetimEtiketi'ne eklenmeli. Aksi hâlde buradaki kural
                // sessizce onu kişi adı sanar ve maskeler.
                //
                // ⚠️ "Sipariş " öneki ESKİ SATIRLAR için: etiket eskiden
                // iki noktasız yazılıyordu ("Sipariş SP-260814-1") ve o
                // satırlar veritabanında duruyor. Bugünkü kod iki noktalı
                // yazıyor; bu koşul yalnızca geçmişi koruyor.
                await _context.AuditLogs
                    .Where(l => l.TargetUserId == userId
                             && !l.TargetName.Contains(":")
                             && !l.TargetName.StartsWith("Sipariş "))
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.TargetName, maskeliAd));

                // ---------- 8) KULLANICIYI ANONİMLEŞTİR ----------

                kullanici.FullName = "Silinmiş Kullanıcı";

                // E-posta benzersiz olmak ZORUNDA — bugün eklediğimiz
                // IX_Users_Email indeksi var. Id'yi kullanmak benzersizliği
                // garanti ediyor çünkü Id zaten birincil anahtar.
                //
                // ".local" alan adı RFC 6762'ye göre yalnızca yerel ağda
                // kullanılır, internette çözümlenemez. Yani bu adrese
                // yanlışlıkla mail gönderilse bile hiçbir yere ulaşmaz.
                kullanici.Email = $"silinmis_{userId}@silinmis.local";

                // ⭐ YENİ — PROFİL FOTOĞRAFI DA GİDİYOR.
                //
                // ⚠️ Ad ve e-postayı maskeleyip fotoğrafı bırakmak,
                // maskelemenin amacını boşa çıkarırdı: kimliği
                // silinmiş bir kaydın YÜZÜ sunucuda durmaya devam
                // ederdi. Dosya aşağıda, transaction commit olduktan
                // sonra diskten de siliniyor.
                var silinecekFoto = kullanici.ProfilFotoUrl;
                kullanici.ProfilFotoUrl = null;

                // Şifreyi rastgele bir değerle değiştiriyoruz.
                //
                // Neden boş bırakmıyoruz? BCrypt.Verify boş hash ile
                // çağrılırsa exception atar. Rastgele bir GUID'in hash'i
                // ise geçerli bir hash — sadece kimse o "şifreyi" bilmiyor.
                // Kimse tahmin edemez çünkü hiçbir yerde saklanmıyor.
                kullanici.PasswordHash =
                    BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());

                // Soft delete işareti — Login bu kontrole bakıp reddediyor.
                kullanici.IsActive = false;

                // Damgayı yenile → eldeki tüm access token'lar anında ölür.
                // Adım 6'da refresh token'ları sildik, burada access'leri
                // öldürüyoruz. İkisi birlikte tam çıkış demek.
                //
                // ⚠️ "Anında" GuvenlikDamgasiMiddleware sayesinde
                // (Program.cs). Middleware kapalıyken kapatılmış hesabın
                // token'ı 15 dakika daha çalışıyordu — KVKK açısından
                // en can sıkıcı dal buydu.
                kullanici.SecurityStamp = Guid.NewGuid().ToString();

                // Bekleyen doğrulama/sıfırlama linklerini iptal et.
                // Kalsalardı: kullanıcı eski bir "şifre sıfırlama" mailindeki
                // linke tıklayıp kapatılmış hesabın şifresini belirleyebilirdi.
                kullanici.EmailDogrulamaTokenHash = null;
                kullanici.EmailDogrulamaTokenBitis = null;
                kullanici.SifreSifirlamaTokenHash = null;
                kullanici.SifreSifirlamaTokenBitis = null;

                // Kilitleme sayaçlarını temizle — artık anlamsız.
                kullanici.YanlisGirisSayisi = 0;
                kullanici.KilitBitis = null;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // ⚠️ Dosya COMMIT'TEN SONRA siliniyor. Önce silseydik ve
                // transaction geri alınsaydı, hesabı duran bir
                // kullanıcının fotoğrafı yok olurdu — geri alınamayan
                // bir yan etki.
                ResimDosyasi.DiskDosyasiniSil(_env, silinecekFoto);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                // ⭐ DEĞİŞTİ — ex.Message ARTIK İSTEMCİYE GİTMİYOR.
                //
                // ⚠️ Bu catch, global HataYakalamaMiddleware'i atlıyor;
                // middleware ise hata detayını yalnızca Development'ta
                // gösterip canlıda gizliyor. Buradaki `hata = ex.Message`
                // o korumayı devre dışı bırakıyor ve veritabanı şemasını
                // (tablo/kolon/kısıt adları) dışarı sızdırıyordu.
                //
                // ⚠️ Mesajın kendisi KALIYOR: "hiçbir değişiklik
                // yapılmadı" cümlesi kullanıcı için gerçek bir bilgi —
                // hesabının yarı silinmiş olmadığını söylüyor.
                // Middleware'in genel mesajı bunu veremezdi.
                _log.LogError(ex, "Hesap kapatılamadı. userId: {UserId}", userId);

                return StatusCode(500, new
                {
                    mesaj = "Hesap kapatılırken hata oldu, hiçbir değişiklik yapılmadı."
                });
            }

            // ---------- 8) VEDA MAİLİ ----------
            //
            // Transaction'ın DIŞINDA: mail gönderilemezse hesap kapatma
            // GEÇERLİ kalmalı. Mail bir bildirimdir, işlemin parçası değil.
            //
            // ⭐ DEĞİŞTİ — çıplak `catch { }` yerine GuvenliGonderAsync.
            // Gerekçe şifre değiştirmedeki notta.
            //
            // ⚠️ Alıcı `eskiEmail`: kullanıcının e-postası yukarıda
            // maskelendi, kaydın kendisinden okunamaz. Sarmalayıcı boş
            // alıcıyı zaten atlıyor.
            var vedaGovdesi =
                $"<p>Merhaba {eskiAd},</p>" +
                "<p>Hesabın kapatıldı ve kişisel bilgilerin " +
                "(adresler, kartlar, sepet, favoriler) silindi.</p>" +
                "<p>Yasal saklama yükümlülüğü nedeniyle geçmiş sipariş " +
                "kayıtların muhasebe kaydı olarak saklanmaya devam eder, " +
                "ancak artık kimliğinle ilişkilendirilemez.</p>" +
                "<p>Bu işlemi sen yapmadıysan hemen bizimle iletişime geç.</p>";

            await _email.GuvenliGonderAsync(
                _log,
                eskiEmail,
                new ETicaretAPI.Services.EmailIcerik("Hesabın Kapatıldı", vedaGovdesi),
                "HesapKapatildi");

            return Ok(new
            {
                mesaj = "Hesabın kapatıldı. Kişisel bilgilerin silindi, " +
                        "geçmiş siparişlerin muhasebe kaydı olarak saklanıyor."
            });
        }


        // ⭐ YENİ — POST /api/auth/oturumlarim
        //
        // Kullanıcının AKTİF oturumlarını listeler.
        //
        // NEDEN GET DEĞİL POST?
        //   İstek gövdesinde refresh token taşıyoruz — "hangi satır bu
        //   cihaz" sorusunu cevaplamak için gerekli.
        //
        //   Sorgu dizesine koysaydık (?refreshToken=...) o sır sunucu
        //   erişim günlüklerine, proxy loglarına ve tarayıcı geçmişine
        //   yazılırdı. GET gövdesi ise HTTP'de tanımsız (hesap kapatmada
        //   da bu yüzden POST seçtik).
        //
        // ⚠️ TokenHash CEVAPTA DÖNMÜYOR. Hash'ten ham token üretilemez ama
        //    yine de sızdırmıyoruz: dışarıya sadece işi olan veri gider.
        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpPost("oturumlarim")]
        public async Task<IActionResult> Oturumlarim([FromBody] RefreshRequestDto dto)
        {
            var userId = GetUserId();
            var simdi = DateTime.UtcNow;

            // İstemci refresh token gönderdiyse hash'ini hesapla.
            // Göndermezse (boş string) hiçbir satır "bu cihaz" işaretlenmez —
            // liste yine çalışır, sadece etiket olmaz.
            string? buCihazHash = string.IsNullOrWhiteSpace(dto.RefreshToken)
                ? null
                : _tokenService.Hashle(dto.RefreshToken);

            // Aktif = iptal edilmemiş VE süresi dolmamış.
            //
            // RefreshToken modelinde bunu anlatan bir "Aktif" özelliği var
            // ama [NotMapped] — yani veritabanı kolonu değil, C# hesaplaması.
            // EF Core onu SQL'e çeviremez, o yüzden koşulu burada elle
            // yazıyoruz. (Kupon durumunda da aynı durumla karşılaşmıştık.)
            var oturumlar = await _context.RefreshTokens
                .Where(t => t.UserId == userId
                         && t.RevokedAt == null
                         && t.ExpiresAt > simdi)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new
                {
                    t.Id,
                    t.CihazBilgisi,
                    t.CreatedAt,
                    t.ExpiresAt,

                    // Hash karşılaştırması SQL'de yapılıyor (EF bunu CASE
                    // WHEN'e çeviriyor). Belleğe çekip karşılaştırmaya gerek yok.
                    buCihaz = buCihazHash != null && t.TokenHash == buCihazHash
                })
                .ToListAsync();

            return Ok(new
            {
                oturumlar = oturumlar,
                toplam = oturumlar.Count
            });
        }


        // ⭐ YENİ — POST /api/auth/oturum-iptal/5
        //
        // Tek bir oturumu kapatır. "Bu cihazı tanımıyorum" durumu için.
        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpPost("oturum-iptal/{id}")]
        public async Task<IActionResult> OturumIptal(int id)
        {
            var userId = GetUserId();

            // ⚠️⚠️ BURADAKİ "&& t.UserId == userId" HAYATİ ÖNEMDE.
            //
            // Olmasaydı: giriş yapmış HERHANGİ bir kullanıcı, id'leri
            // deneyerek BAŞKASININ oturumunu kapatabilirdi. 1'den 1000'e
            // kadar bir döngü yazan biri tüm kullanıcıları sistemden atardı.
            //
            // Bu açığın adı IDOR (Insecure Direct Object Reference) —
            // "güvensiz doğrudan nesne referansı". OWASP'ın en sık görülen
            // açıkları listesinde üst sıralarda.
            //
            // Kural: bir kaydı id ile getirirken SAHİPLİK kontrolü de aynı
            // sorguya girmeli. Ayrı bir if ile sonradan kontrol etmek de
            // olur ama tek sorguda yapmak unutma riskini sıfırlıyor.
            var oturum = await _context.RefreshTokens
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (oturum == null)
            {
                // Başkasının oturumu da buraya düşer ve "bulunamadı" der.
                // "Bu senin değil" demek, o id'de bir kaydın VAR olduğunu
                // sızdırırdı. Yokmuş gibi davranmak daha güvenli.
                return NotFound(new { mesaj = "Oturum bulunamadı." });
            }

            if (oturum.RevokedAt != null)
            {
                // Zaten kapalı. Hata değil — kullanıcı iki sekmeden aynı
                // butona basmış olabilir. İdempotent davranıyoruz.
                return Ok(new { mesaj = "Bu oturum zaten kapatılmış." });
            }

            oturum.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Oturum kapatıldı." });
        }


        // ⭐ YENİ — POST /api/auth/diger-oturumlari-kapat
        //
        // Bu cihaz HARİÇ tüm oturumları kapatır.
        // "Şüpheli bir şey var, her yerden çıkış yapayım ama burada kalayım."
        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpPost("diger-oturumlari-kapat")]
        public async Task<IActionResult> DigerOturumlariKapat(
            [FromBody] RefreshRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.RefreshToken))
            {
                // Refresh token olmadan "hangisini koruyacağımızı" bilemeyiz.
                // Boş gelirse hepsini kapatmak, kullanıcıyı kendi tıkladığı
                // cihazdan da atmak olurdu — istenmeyen sürpriz.
                return BadRequest(new
                {
                    mesaj = "Bu cihaz belirlenemedi. Sayfayı yenileyip tekrar dene."
                });
            }

            var userId = GetUserId();
            var buCihazHash = _tokenService.Hashle(dto.RefreshToken);
            var simdi = DateTime.UtcNow;

            // ExecuteUpdateAsync: tek SQL cümlesiyle güncelliyor.
            //
            // Klasik yol: hepsini belleğe çek → foreach ile RevokedAt ata →
            //             SaveChanges → EF her satır için ayrı UPDATE
            // Bu yol:     UPDATE RefreshTokens SET RevokedAt = @simdi
            //             WHERE UserId = @id AND RevokedAt IS NULL
            //               AND TokenHash <> @buCihaz
            //
            // 30 oturumu olan kullanıcıda 31 sorgu yerine 1 sorgu.
            // Stok ve kupon düzeltmelerinde kullandığımız aracın aynısı.
            var etkilenen = await _context.RefreshTokens
                .Where(t => t.UserId == userId
                         && t.RevokedAt == null
                         && t.TokenHash != buCihazHash)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    t => t.RevokedAt,
                    (DateTime?)simdi));

            return Ok(new
            {
                mesaj = etkilenen == 0
                    ? "Kapatılacak başka oturum yoktu."
                    : $"{etkilenen} oturum kapatıldı.",
                kapatilan = etkilenen
            });
        }


        // ---------- YARDIMCILAR ----------

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


        // ==========================================================
        //  ADMİN BAŞVURUSU
        // ==========================================================

        // ⭐ Reddedilen kişi ne kadar bekleyecek? Bekleme süresi ayardan geliyor.
        //
        // Neden const değil property?
        // const derleme zamanında sabitlenir; ayar ise çalışma
        // zamanında okunur. İkisi bir arada olamaz.
        //
        // Neden => (ifade gövdeli özellik)?
        // Her okunduğunda config'e bakıyor. Bir kez okuyup alanda
        // saklasaydık, appsettings çalışırken değiştirildiğinde
        // (ASP.NET bunu destekler) eski değer kullanılmaya devam
        // ederdi.
        //
        // GetValue<int> yerine ?? 3:
        // Ayar hiç yoksa veya bozuksa sistem çökmemeli, makul bir
        // varsayılana düşmeli. "Yapılandırma eksikse çalışma" değil,
        // "yapılandırma eksikse güvenli varsayılanla çalış."
        private int RedSonrasiBeklemeSaati =>
            _config.GetValue<int?>("Basvuru:RedSonrasiBeklemeSaati") ?? 3;

        // 🟢 POST /api/auth/admin-basvuru
        //
        // Herkese açık ama şifre doğruluyor: başvuran, o hesabın
        // sahibi olduğunu kanıtlamak zorunda.
        [EnableRateLimiting("basvuru")]
        [HttpPost("admin-basvuru")]
        public async Task<IActionResult> AdminBasvuru([FromBody] AdminBasvuruCreateDto dto)
        {
            // ⚠️ TEK CEVAP — HER DURUMDA AYNI.
            //
            // Hesap yok, şifre yanlış, kişi zaten admin, bekleyen
            // başvurusu var, 30 gün dolmamış... Hepsinde bu cevap
            // dönüyor.
            //
            // Neden? Farklı cevap verirsek saldırgan bu ucu bir
            // TARAYICI gibi kullanır:
            //   "hesap bulunamadı"      → bu e-posta kayıtlı DEĞİL
            //   "zaten adminsin"        → bu e-posta bir ADMİN
            //   "şifre yanlış"          → bu e-posta KAYITLI
            // Üçü de sızıntı. forgot-password'de aynı kararı verdik.
            //
            // Bedeli: gerçekten başvuran biri, başvurusunun neden
            // işlenmediğini bilemez. Kabul ediyoruz — bu uç ömürde
            // bir kez kullanılan bir uç.
            var standartCevap = Ok(new
            {
                mesaj = "Başvurunuz alındı. Değerlendirme sonucu e-posta ile bildirilecektir."
            });

            var email = dto.Email.Trim().ToLowerInvariant();

            var kullanici = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == email);

            // ---- KİMLİK KONTROLLERİ ----
            //
            // Hepsi tek bir if'te toplanabilirdi ama ayrı ayrı
            // yazmak hangi kuralın neyi koruduğunu okunur kılıyor.

            if (kullanici == null)
            {
                return standartCevap;
            }

            // Pasifleştirilmiş hesap başvuramaz.
            if (!kullanici.IsActive)
            {
                return standartCevap;
            }

            // ⚠️ E-posta doğrulanmamışsa başvuru kabul edilmez.
            // Doğrulanmamış adres, o adresin sahibi olduğunu
            // kanıtlamıyor demektir — admin yetkisi için yetersiz.
            if (!kullanici.EmailDogrulandiMi)
            {
                return standartCevap;
            }

            // Şifre kontrolü: hesabın sahibi mi?
            if (!BCrypt.Net.BCrypt.Verify(dto.Sifre, kullanici.PasswordHash))
            {
                return standartCevap;
            }

            // ---- İŞ KURALLARI ----

            // Zaten admin veya süperadmin olan başvuramaz.
            if (kullanici.Role != "customer")
            {
                return standartCevap;
            }

            // Bekleyen başvurusu var mı?
            //
            // ⚠️ Bu kontrol GARANTİ DEĞİL — iki eşzamanlı istek
            // ikisi de "yok" görebilir. Garantiyi aşağıdaki
            // DbUpdateException yakalaması (filtreli unique index)
            // veriyor. Bu if sadece ucuz durumu ucuza halleder.
            var bekleyenVar = await _context.AdminBasvurular
                .AnyAsync(b => b.UserId == kullanici.Id
                            && b.Durum == BasvuruDurumu.Beklemede);

            if (bekleyenVar)
            {
                return standartCevap;
            }

            // Son 30 günde reddedilmiş mi?
            //
            // Neden bekleme süresi var? Reddedilen kişi her gün
            // yeniden başvurup süperadmini yıldırabilir. Ret bir
            // karardır, tekrar tekrar sorulmamalı.
            // ⭐ DEĞİŞTİ — gün değil saat.
            //
            // Bekleme 0 (veya negatif) ayarlanmışsa kural tamamen
            // devre dışı: eşik "şu an"a eşit olur ve hiçbir geçmiş
            // kayıt ondan büyük olamaz.
            var esik = DateTime.UtcNow.AddHours(-RedSonrasiBeklemeSaati);

            var yakinRet = await _context.AdminBasvurular
                .AnyAsync(b => b.UserId == kullanici.Id
                            && b.Durum == BasvuruDurumu.Reddedildi
                            && b.KararTarihi != null
                            && b.KararTarihi > esik);

            if (yakinRet)
            {
                return standartCevap;
            }

            // ---- KAYDI OLUŞTUR ----
            _context.AdminBasvurular.Add(new AdminBasvuru
            {
                UserId = kullanici.Id,
                Gerekce = dto.Gerekce.Trim(),
                Durum = BasvuruDurumu.Beklemede,
                CreatedAt = DateTime.UtcNow
            });

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Filtreli unique index devreye girdi: bu kullanıcının
                // zaten bekleyen bir başvurusu varmış (yarış koşulu).
                //
                // Kullanıcı açısından hiçbir şey değişmiyor — başvurusu
                // zaten sistemde. Aynı cevabı dönüyoruz.
                return standartCevap;
            }

            return standartCevap;
        }

    }
}