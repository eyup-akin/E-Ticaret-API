using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;

namespace ETicaretAPI.Services
{
    // ============================================================
    //  STOK BİLDİRİM SERVİSİ — "stoka gelince haber ver"
    //
    //  Bekleyen istekleri tarar, ürünü tekrar satılabilir hale
    //  gelmiş olanlara e-posta gönderir ve kaydı damgalar.
    //
    //  ⚠️ NEDEN PERİYODİK TARAMA, NEDEN STOK YAZMA ANINDA DEĞİL?
    //
    //  Yol haritasının ilk fikri "stok defterine bağla, pozitif
    //  hareket yazılınca kontrol et" idi. Uygulanabilir değil:
    //  StokDefteri bilerek SaveChanges ÇAĞIRMIYOR (hareketin
    //  tetikleyen işlemle aynı transaction'da yazılması için).
    //  Yani defter, "stok gerçekten arttı mı" anını göremiyor —
    //  o an SaveChanges'ten sonra.
    //
    //  Geriye iki seçenek kalıyordu:
    //
    //   1) Stoğu artıran HER yere çağrı koymak. Beş yer var: ürün
    //      ekleme, ürün güncelleme, Excel içe aktarma, müşteri
    //      iptali, admin iptali. Tam olarak StokDefteri'nin
    //      yorumunda "biri unutulur, defterde sessiz boşluk kalır"
    //      diye uyarılan desen — ve burada unutulan yer, müşterinin
    //      hiç mail almaması demek. Sessiz başarısızlık.
    //
    //   2) Periyodik tarama. Tek nokta; hiçbir yazma yolu bunu
    //      atlayamaz çünkü hiçbir yazma yoluna bağlı değil.
    //
    //  İkincisi seçildi. Bedeli gecikme: müşteri stok girdikten
    //  sonra en fazla tarama aralığı kadar bekliyor. "Stoka geldi"
    //  bildirimi için birkaç dakika kabul edilebilir; sessizce hiç
    //  gitmeyen bir mail değil.
    //
    //  ⚠️ TARAMA STOĞU YENİDEN OKUYOR — bu kasıtlı bir güvence.
    //  Kararı "şu an stokta mı?" sorusuna göre veriyoruz, geçmişte
    //  bir hareket olup olmadığına göre değil. Böylece stok girip
    //  hemen tükenirse (ya da artıran transaction geri alınırsa)
    //  yanlış bildirim gitmiyor.
    // ============================================================
    public class StokBildirimServisi
    {
        private readonly AppDbContext _context;
        private readonly IEmailGonderici _email;
        private readonly EmailSablonlari _sablonlar;
        private readonly ILogger<StokBildirimServisi> _logger;

        public StokBildirimServisi(
            AppDbContext context,
            IEmailGonderici email,
            EmailSablonlari sablonlar,
            ILogger<StokBildirimServisi> logger)
        {
            _context = context;
            _email = email;
            _sablonlar = sablonlar;
            _logger = logger;
        }

        // ------------------------------------------------------------
        //  Hangfire'ın çağırdığı giriş noktası.
        //
        //  ⚠️ public ve parametresiz olmalı — Hangfire metodu adıyla
        //  serileştirip sonra yansımayla (reflection) çağırıyor.
        // ------------------------------------------------------------
        public async Task BekleyenleriGonderAsync()
        {
            // ---------- 1) HANGİ ÜRÜNLER HAZIR? ----------
            //
            // Bekleyen istekleri, ürünü ARTIK SATILABİLİR olanlarla
            // tek sorguda eşleştiriyoruz.
            //
            // ⚠️ IsActive de şart, sadece Stock > 0 yetmez. Satıştan
            // kaldırılmış bir ürün için "stoka geldi" maili atmak,
            // müşteriyi satın alamayacağı bir sayfaya göndermek olurdu.
            var gonderilecekler = await (
                from uyari in _context.StockAlerts
                join urun in _context.Products on uyari.ProductId equals urun.Id
                join kullanici in _context.Users on uyari.UserId equals kullanici.Id
                where uyari.NotifiedAt == null
                      && urun.Stock > 0
                      && urun.IsActive
                select new
                {
                    UyariId = uyari.Id,
                    UrunAdi = urun.Name,
                    UrunId = urun.Id,
                    kullanici.Email,
                    kullanici.FullName
                })
                .ToListAsync();

            if (gonderilecekler.Count == 0)
            {
                return;
            }

            _logger.LogInformation(
                "Stok bildirimi: {Adet} bekleyen istek gönderilecek.",
                gonderilecekler.Count);

            // ---------- 2) ÖNCE DAMGALA, SONRA GÖNDER ----------
            //
            // ⚠️ SIRA ÖNEMLİ VE BİLEREK BÖYLE.
            //
            // Önce mail atıp sonra damgalasaydık, mail gittikten sonra
            // uygulama çökerse damga yazılmazdı; bir sonraki tarama
            // AYNI müşteriye AYNI maili tekrar gönderirdi. Tarama
            // dakikada bir çalıştığı için bu, müşterinin gelen
            // kutusunu doldurmak demekti.
            //
            // Bu sırada risk şu: damga yazıldı ama mail gitmedi →
            // müşteri hiç haber alamaz. İkisinden birini seçmek
            // zorundayız (dağıtık işlemlerin klasik problemi) ve
            // "bir kez eksik" , "yüzlerce kez fazla"dan iyidir.
            var idler = gonderilecekler.Select(x => x.UyariId).ToList();
            var simdi = DateTime.UtcNow;

            await _context.StockAlerts
                .Where(s => idler.Contains(s.Id) && s.NotifiedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NotifiedAt, simdi));

            // ---------- 3) MAİLLERİ GÖNDER ----------
            //
            // GuvenliGonderAsync zaten kendi içinde yutuyor; bir
            // müşterinin maili patlarsa diğerleri etkilenmiyor.
            foreach (var kayit in gonderilecekler)
            {
                var icerik = _sablonlar.StokaGeldi(
                    kayit.FullName,
                    kayit.UrunAdi,
                    kayit.UrunId);

                await _email.GuvenliGonderAsync(
                    _logger,
                    kayit.Email,
                    icerik,
                    "StokaGeldi");
            }
        }
    }
}
