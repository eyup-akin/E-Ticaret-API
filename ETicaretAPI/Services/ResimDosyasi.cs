namespace ETicaretAPI.Services
{
    // ⭐ YENİ — YÜKLENEN RESİM DOSYASININ KURALLARI, TEK YERDE
    //
    // ⚠️ NEDEN ORTAK YERE TAŞINDI?
    //
    // Boyut sınırı, izinli uzantılar ve "byte'lara bakarak gerçekten
    // resim mi" kontrolü ProductsController'ın içinde private yazılıydı
    // ve tek tüketicisi vardı. Banner yükleme ikinci tüketici oldu.
    //
    // Kopyalasaydık ayrışacak yer belliydi: sınırı 5 MB'tan 8 MB'a
    // çıkaran biri iki dosyadan yalnızca birini değiştirir, avif
    // desteği ekleyen biri diğerini unuturdu. İkisi de patlamayan,
    // yalnızca "bir yerde çalışıp bir yerde çalışmayan" hatalar.
    public static class ResimDosyasi
    {
        public const long MaxBoyut = 5 * 1024 * 1024; // 5 MB

        public static readonly string[] IzinliUzantilar = { ".jpg", ".jpeg", ".png", ".webp" };
        public static readonly string[] IzinliTipler = { "image/jpeg", "image/png", "image/webp" };

        // Yüklenen dosyayı baştan sona doğrular.
        // null = sorun yok, dolu metin = kullanıcıya gösterilecek hata.
        //
        // ⚠️ Üç kontrol de gerekli ve sırası önemli:
        //   uzantı   → istemciden gelir, yalan olabilir
        //   MIME tipi→ istemciden gelir, yalan olabilir
        //   byte'lar → dosyanın kendisidir, yalan söylenemez
        // İlk ikisi ucuz eleme, üçüncüsü gerçek kontrol.
        public static async Task<string?> DogrulaAsync(IFormFile? dosya)
        {
            if (dosya == null || dosya.Length == 0)
            {
                return "Dosya seçilmedi!";
            }

            if (dosya.Length > MaxBoyut)
            {
                return "Dosya en fazla 5 MB olabilir!";
            }

            var uzanti = Path.GetExtension(dosya.FileName).ToLowerInvariant();

            if (!IzinliUzantilar.Contains(uzanti))
            {
                return "Sadece jpg, jpeg, png ve webp yüklenebilir!";
            }

            if (!IzinliTipler.Contains(dosya.ContentType.ToLowerInvariant()))
            {
                return "Geçersiz dosya tipi!";
            }

            if (!await GercektenResimMi(dosya))
            {
                return "Dosya gerçek bir resim değil!";
            }

            return null;
        }

        // GERÇEK KONTROL: dosyanın İÇİNE bak.
        // Uzantı ve ContentType istemciden gelir → yalan olabilir.
        // İlk byte'lar dosyanın kendisindedir → yalan söylenemez.
        public static async Task<bool> GercektenResimMi(IFormFile dosya)
        {
            using var akis = dosya.OpenReadStream();

            var baslik = new byte[12];
            var okunan = await akis.ReadAsync(baslik, 0, 12);

            if (okunan < 12)
            {
                return false; // 12 byte bile yoksa resim değildir
            }

            return UzantiBul(baslik) != null;
        }

        // Ham byte'lardan gerçek biçimi bulur ve doğru uzantıyı döndürür.
        // Resim değilse null. (Uzantıyı adrese değil İÇERİĞE soruyoruz.)
        public static string? UzantiBul(byte[] veri)
        {
            if (veri.Length < 12)
            {
                return null;
            }

            // JPEG: FF D8 FF
            if (veri[0] == 0xFF && veri[1] == 0xD8 && veri[2] == 0xFF)
            {
                return ".jpg";
            }

            // PNG: 89 50 4E 47
            if (veri[0] == 0x89 && veri[1] == 0x50 && veri[2] == 0x4E && veri[3] == 0x47)
            {
                return ".png";
            }

            // WEBP: "RIFF" .... "WEBP"
            if (veri[0] == 0x52 && veri[1] == 0x49 && veri[2] == 0x46 && veri[3] == 0x46 &&
                veri[8] == 0x57 && veri[9] == 0x45 && veri[10] == 0x42 && veri[11] == 0x50)
            {
                return ".webp";
            }

            return null;
        }

        // wwwroot'un diskteki tam yolu.
        public static string WebKok(IWebHostEnvironment env)
        {
            return string.IsNullOrEmpty(env.WebRootPath)
                ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
                : env.WebRootPath;
        }

        // Yüklenen dosyayı diske yazar, web yolunu döndürür.
        //
        // ⚠️ Kullanıcının gönderdiği dosya adı ASLA kullanılmıyor:
        // aynı isimli dosyanın üstüne yazma ve "../../" yol saldırısı
        // riski var. Ad her seferinde Guid.
        public static async Task<string> DiskeYazAsync(
            IWebHostEnvironment env, IFormFile dosya, string altKlasor)
        {
            var klasor = Path.Combine(WebKok(env), "uploads", altKlasor);
            Directory.CreateDirectory(klasor); // varsa dokunmaz

            var uzanti = Path.GetExtension(dosya.FileName).ToLowerInvariant();
            var yeniAd = Guid.NewGuid().ToString("N") + uzanti;

            using (var akis = new FileStream(Path.Combine(klasor, yeniAd), FileMode.Create))
            {
                await dosya.CopyToAsync(akis);
            }

            return "/uploads/" + altKlasor + "/" + yeniAd;
        }

        // Diskteki fiziksel dosyayı siler (yoksa sessizce geçer).
        //
        // ⚠️ Yalnızca /uploads altındaki yollar siliniyor. Çağıran
        // yerin veritabanından okuduğu bir yol geliyor ama bir gün
        // dışarıdan gelen bir metin buraya düşerse "../appsettings.json"
        // silinmesin.
        public static void DiskDosyasiniSil(IWebHostEnvironment env, string? url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("/uploads/"))
            {
                return;
            }

            // "/uploads/bannerlar/a.jpg" → "uploads\bannerlar\a.jpg"
            var goreliYol = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var tamYol = Path.Combine(WebKok(env), goreliYol);

            if (File.Exists(tamYol))
            {
                File.Delete(tamYol);
            }
        }
    }
}
