using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETicaretAPI.Controllers
{
    [Route("api/odeme")]
    [ApiController]
    public class OdemeController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IOdemeSaglayici _saglayici;
        private readonly OdemeAyarlari _ayarlar;
        private readonly IyzicoSepetiKurucu _sepetKurucu;
        private readonly OdemeSonucIsleyici _isleyici;
        private readonly ILogger<OdemeController> _log;

        public OdemeController(
            AppDbContext context,
            IOdemeSaglayici saglayici,
            OdemeAyarlari ayarlar,
            IyzicoSepetiKurucu sepetKurucu,
            OdemeSonucIsleyici isleyici,
            ILogger<OdemeController> log)
        {
            _context = context;
            _saglayici = saglayici;
            _ayarlar = ayarlar;
            _sepetKurucu = sepetKurucu;
            _isleyici = isleyici;
            _log = log;
        }

        private int GetUserId() =>
            int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);


        // ============================================================
        //  POST /api/odeme/baslat — ödeme sayfası adresi üretir
        // ============================================================
        [Authorize]
        [HttpPost("baslat")]
        public async Task<IActionResult> Baslat([FromBody] OdemeBaslatDto dto)
        {
            var userId = GetUserId();

            // ⚠️ Sahiplik SORGUYA dahil — ayrı bir if unutulabilir.
            var siparis = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == dto.SiparisId && o.UserId == userId);

            if (siparis == null)
            {
                return NotFound(new { mesaj = "Sipariş bulunamadı." });
            }

            if (siparis.Status != SiparisDurumlari.OdemeBekliyor)
            {
                return BadRequest(new
                {
                    mesaj = siparis.PaymentStatus == OdemeDurumlari.Odendi
                        ? "Bu siparişin ödemesi zaten alınmış."
                        : "Bu sipariş artık ödenemez."
                });
            }

            // ⚠️ İnceleme sürerken yeni deneme açılmıyor: para çekilmiş
            // olabilir, ikinci deneme çift ödeme demek.
            if (siparis.PaymentStatus == OdemeDurumlari.Incelemede)
            {
                return BadRequest(new
                {
                    mesaj = "Ödemen banka doğrulamasında. Sonucu bekle."
                });
            }

            var kullanici = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (kullanici == null)
            {
                return Unauthorized(new { mesaj = "Kullanıcı bulunamadı." });
            }

            // Kalem adları donmuş halde siparişte; kategori adı için
            // ürüne bakılıyor, yoksa "Genel".
            var kalemler = await _context.OrderItems
                .Where(oi => oi.OrderId == siparis.Id)
                .Select(oi => new
                {
                    oi.Id,
                    oi.ProductName,
                    oi.UnitPrice,
                    oi.Quantity,
                    Kategori = _context.Products
                        .Where(p => p.Id == oi.ProductId)
                        .Select(p => _context.Categories
                            .Where(c => c.Id == p.CategoryId)
                            .Select(c => c.Name).FirstOrDefault())
                        .FirstOrDefault()
                })
                .ToListAsync();

            if (kalemler.Count == 0)
            {
                return BadRequest(new { mesaj = "Siparişin kalemi yok." });
            }

            IyzicoSepetSonucu sepet;

            try
            {
                sepet = _sepetKurucu.Kur(
                    kalemler.Select(k => new IyzicoKalemGirdisi(
                        k.Id, k.ProductName, k.Kategori ?? "Genel",
                        k.UnitPrice * k.Quantity)).ToList(),
                    siparis.ShippingCost,
                    siparis.Total);
            }
            catch (Exception ex)
            {
                // ⚠️ Buraya düşmek "sepet toplamı tutmuyor" demek ve
                // iyzico isteği tümden reddederdi. Sessizce yanlış tutar
                // göndermek yerine burada duruyoruz.
                _log.LogError(ex, "iyzico sepeti kurulamadı. siparisId: {Id}", siparis.Id);
                return StatusCode(500, new { mesaj = "Ödeme sepeti hazırlanamadı." });
            }

            var conversationId = "sp" + siparis.Id + "-" + Guid.NewGuid().ToString("N")[..12];

            // Kayıt ÖNCE açılıyor: iyzico cevabı gelmese bile denemenin
            // izi kalsın.
            var islem = new OdemeIslemi
            {
                OrderId = siparis.Id,
                UserId = userId,
                ConversationId = conversationId,
                Token = string.Empty,
                Durum = OdemeDurumlari.DenemeBaslatildi,
                Price = siparis.Total,
                ParaBirimi = "TRY"
            };

            var adSoyad = AdiBol(siparis.ShippingFullName, kullanici.FullName);

            var istek = new OdemeBaslatIstegi(
                ConversationId: conversationId,
                SiparisId: siparis.Id,
                Tutar: siparis.Total,
                Kalemler: sepet.Kalemler,
                Taksitler: _ayarlar.TaksitSecenekleri(siparis.Total),
                CallbackUrl: $"{_ayarlar.TabanAdres}/api/odeme/callback",
                Alici: new OdemeAlicisi(
                    KullaniciId: userId.ToString(),
                    Ad: adSoyad.Ad,
                    Soyad: adSoyad.Soyad,
                    Email: kullanici.Email,
                    Telefon: GsmBicimi(siparis.ShippingPhone),

                    // ⚠️ TC kimlik toplamıyoruz; sandbox sabit değeri
                    // kabul ediyor. Canlıya çıkarken gerçekten
                    // toplanması gerekiyor (açık borç).
                    KimlikNo: "11111111111",
                    Adres: siparis.ShippingFullAddress,
                    Sehir: siparis.ShippingCity,
                    Ip: ETicaretAPI.Support.IstemciAdresi.Oku(HttpContext),
                    KayitTarihi: kullanici.CreatedAt),
                TeslimatAdresi: new OdemeAdresi(
                    AliciAdi: siparis.ShippingFullName,
                    Sehir: siparis.ShippingCity,
                    Adres: siparis.ShippingFullAddress),
                CardUserKey: kullanici.IyzicoCardUserKey);

            var sonuc = await _saglayici.BaslatAsync(istek);

            if (!sonuc.Basarili || string.IsNullOrWhiteSpace(sonuc.Token))
            {
                islem.Token = "hata-" + Guid.NewGuid().ToString("N")[..16];
                islem.Durum = OdemeDurumlari.DenemeBasarisiz;
                islem.HataKodu = sonuc.HataKodu;
                islem.HataMesaji = sonuc.HataMesaji;
                islem.HamCevap = sonuc.HamCevap;
                islem.TamamlanmaZamani = DateTime.UtcNow;

                _context.OdemeIslemleri.Add(islem);
                await _context.SaveChangesAsync();

                return StatusCode(502, new
                {
                    mesaj = sonuc.HataMesaji ?? "Ödeme başlatılamadı, tekrar dener misin?"
                });
            }

            islem.Token = sonuc.Token!;
            islem.TokenGecerlilik = sonuc.TokenGecerlilik;
            islem.HamCevap = sonuc.HamCevap;

            _context.OdemeIslemleri.Add(islem);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                mesaj = "Ödeme sayfası hazır.",
                siparisId = siparis.Id,
                tutar = siparis.Total,
                odemeSayfasiUrl = sonuc.OdemeSayfasiUrl,
                token = sonuc.Token,

                // Mobil WebView'in "ödeme bitti" diye yakalayacağı adres.
                // ⚠️ Sonuç bu adresten OKUNMUYOR; yalnızca WebView'i
                // kapatma sinyali. Gerçek sonuç /durum ile soruluyor.
                donusAdresi = $"{_ayarlar.TabanAdres}/api/odeme/sonuc"
            });
        }


        // ============================================================
        //  POST /api/odeme/callback — iyzico kullanıcıyı buraya yollar
        //
        //  ⚠️ [AllowAnonymous]: isteği iyzico'nun sayfası gönderiyor,
        //  bizim token'ımız yok. Güvenlik, gelen token'la sunucudan
        //  sorgu yapılmasından geliyor.
        //
        //  ⚠️ [FromForm]: gövde application/x-www-form-urlencoded
        //  geliyor, JSON değil. [FromBody] burada hiç çalışmaz.
        // ============================================================
        [AllowAnonymous]
        [HttpPost("callback")]
        public async Task<IActionResult> Callback([FromForm] string? token)
        {
            var sonuc = await _isleyici.IsleAsync(token ?? "");

            // Kullanıcının tarayıcısı burada; HTML dönüyoruz.
            return SonucSayfasi(sonuc);
        }


        // ============================================================
        //  POST /api/odeme/webhook — sunucudan sunucuya bildirim
        //
        //  ⚠️ Callback kullanıcının tarayıcısından gelir; müşteri 3DS
        //  ekranında uygulamayı kapatırsa HİÇ GELMEZ. Webhook gelene
        //  kadar 3 kez denenir ve aynı idempotent işleyiciyi çağırır.
        // ============================================================
        [AllowAnonymous]
        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook()
        {
            // Gövdeyi ham okuyoruz: imza doğrulaması ham metin üzerinden
            // yapılıyor, model bağlama sonrası yeniden üretmek riskli.
            string govde;

            using (var okuyucu = new StreamReader(Request.Body, Encoding.UTF8))
            {
                govde = await okuyucu.ReadToEndAsync();
            }

            var bildirim = AyristirBildirim(govde);

            // ⚠️ İmza doğrulanamasa bile kayıt tutuluyor: "imzasız
            // bildirim geldi" bilgisi silinmemeli.
            bildirim.ImzaGecerliMi = ImzaGecerliMi(bildirim, govde);
            bildirim.HamGovde = govde;

            try
            {
                _context.IyzicoBildirimleri.Add(bildirim);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // ⚠️ ASIL TEKRAR KORUMASI. Unique index aynı bildirimi
                // ikinci kez kabul etmiyor; if ile kontrol etmek yarış
                // koşuluna açıktı.
                _log.LogInformation("Tekrarlanan iyzico bildirimi yok sayıldı.");
                return Ok(new { mesaj = "Zaten alınmış." });
            }

            if (string.IsNullOrWhiteSpace(bildirim.Token))
            {
                return Ok(new { mesaj = "Token yok, işlenemedi." });
            }

            // ⚠️ Gövdedeki duruma GÜVENİLMİYOR; sonuç her zaman
            // sağlayıcıya sorularak öğreniliyor.
            var sonuc = await _isleyici.IsleAsync(bildirim.Token!);

            bildirim.IslendiMi = sonuc.Tip is OdemeSonucTipi.Basarili
                or OdemeSonucTipi.Incelemede
                or OdemeSonucTipi.Basarisiz
                or OdemeSonucTipi.ZatenIslendi;

            await _context.SaveChangesAsync();

            // ⚠️ iyzico 200 dışındaki her cevapta tekrar deniyor.
            // İşlenemeyen bildirimde de 200 dönüyoruz çünkü tekrar
            // denemek sonucu değiştirmeyecek.
            return Ok(new { mesaj = sonuc.Mesaj });
        }


        // ============================================================
        //  GET /api/odeme/durum/{siparisId} — sonucu SUNUCUDAN sor
        //
        //  ⚠️ Mobil WebView'de gördüğü sayfaya göre karar vermiyor.
        //  Ön yüzün söylediği "ödendi" bilgi değildir.
        // ============================================================
        [Authorize]
        [HttpGet("durum/{siparisId:int}")]
        public async Task<IActionResult> Durum(int siparisId)
        {
            var userId = GetUserId();

            var siparis = await _context.Orders
                .Where(o => o.Id == siparisId && o.UserId == userId)
                .Select(o => new { o.Id, o.Status, o.PaymentStatus, o.Total, o.OrderNumber })
                .FirstOrDefaultAsync();

            if (siparis == null)
            {
                return NotFound(new { mesaj = "Sipariş bulunamadı." });
            }

            var sonDeneme = await _context.OdemeIslemleri
                .Where(o => o.OrderId == siparisId)
                .OrderByDescending(o => o.Id)
                .Select(o => new { o.Durum, o.HataMesaji, o.Taksit, o.Son4Hane })
                .FirstOrDefaultAsync();

            return Ok(new
            {
                siparisId = siparis.Id,
                siparisNo = siparis.OrderNumber,
                durum = siparis.Status,
                odemeDurumu = siparis.PaymentStatus,
                toplam = siparis.Total,
                odendiMi = siparis.PaymentStatus == OdemeDurumlari.Odendi,
                incelemedeMi = siparis.PaymentStatus == OdemeDurumlari.Incelemede,
                denemeDurumu = sonDeneme?.Durum,
                hataMesaji = sonDeneme?.HataMesaji,
                taksit = sonDeneme?.Taksit,
                kartSon4 = sonDeneme?.Son4Hane
            });
        }


        // Ödeme bittiğinde WebView'in yakalayacağı sabit adres.
        [AllowAnonymous]
        [HttpGet("sonuc")]
        public IActionResult Sonuc() =>
            Content("<html><body><p>Odeme tamamlandi.</p></body></html>", "text/html");


        // ============================================================
        //  SİMÜLASYON — yalnızca Saglayici "simulasyon" iken çalışır
        // ============================================================

        [AllowAnonymous]
        [HttpGet("simulasyon")]
        public IActionResult SimulasyonSayfasi([FromQuery] string token)
        {
            if (_saglayici is not SimulasyonSaglayici sim || !sim.TokenVar(token))
            {
                return NotFound();
            }

            var html = $@"<html><head><meta charset='utf-8'><title>Sahte Odeme</title></head>
<body style='font-family:sans-serif;max-width:420px;margin:40px auto'>
<h2>Simulasyon odeme sayfasi</h2>
<p>Bu sayfa iyzico DEGIL. Saglayici 'simulasyon' oldugu icin cikti.</p>
<form method='post' action='/api/odeme/simulasyon/sonuc'>
  <input type='hidden' name='token' value='{token}' />
  <button name='sonuc' value='basarili'>Odemeyi onayla</button>
  <button name='sonuc' value='basarisiz'>Reddet</button>
</form>
</body></html>";

            return Content(html, "text/html");
        }

        [AllowAnonymous]
        [HttpPost("simulasyon/sonuc")]
        public async Task<IActionResult> SimulasyonSonuc(
            [FromForm] string token, [FromForm] string sonuc)
        {
            if (_saglayici is not SimulasyonSaglayici sim)
            {
                return NotFound();
            }

            if (!sim.SonucBelirle(token, sonuc == "basarili"))
            {
                return NotFound(new { mesaj = "Simülasyon token'ı bulunamadı." });
            }

            // Gerçek callback ile AYNI işleyici — simülasyon ayrı bir
            // yol açmıyor, sadece üçüncü çağıran oluyor.
            var islenen = await _isleyici.IsleAsync(token);

            return SonucSayfasi(islenen);
        }


        // ---------- yardımcılar ----------

        private ContentResult SonucSayfasi(OdemeSonucu sonuc)
        {
            var baslik = sonuc.Tip switch
            {
                OdemeSonucTipi.Basarili => "Odeme alindi",
                OdemeSonucTipi.ZatenIslendi => "Odeme alindi",
                OdemeSonucTipi.Incelemede => "Odeme dogrulanıyor",
                OdemeSonucTipi.DevamEdiyor => "Odeme suruyor",
                _ => "Odeme alinamadi"
            };

            // Mobil WebView bu adrese yönlendiği anda kendini kapatıyor.
            var html = $@"<html><head><meta charset='utf-8'>
<meta http-equiv='refresh' content='2;url={_ayarlar.TabanAdres}/api/odeme/sonuc'></head>
<body style='font-family:sans-serif;text-align:center;margin-top:60px'>
<h2>{baslik}</h2><p>{System.Net.WebUtility.HtmlEncode(sonuc.Mesaj)}</p></body></html>";

            return Content(html, "text/html");
        }

        private static (string Ad, string Soyad) AdiBol(string? donmus, string yedek)
        {
            var tam = string.IsNullOrWhiteSpace(donmus) ? yedek : donmus;
            var parcalar = (tam ?? "").Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parcalar.Length == 0)
            {
                // iyzico boş ad/soyad kabul etmiyor.
                return ("Musteri", "Musteri");
            }

            return parcalar.Length == 1
                ? (parcalar[0], parcalar[0])
                : (string.Join(' ', parcalar[..^1]), parcalar[^1]);
        }

        // iyzico gsmNumber'ı +90 biçiminde bekliyor.
        private static string GsmBicimi(string? gosterilen)
        {
            var kanonik = TelefonBicimi.Normalize(gosterilen);
            return kanonik == null ? "+905000000000" : "+90" + kanonik;
        }

        private static IyzicoBildirimi AyristirBildirim(string govde)
        {
            var bildirim = new IyzicoBildirimi();

            try
            {
                using var belge = System.Text.Json.JsonDocument.Parse(govde);
                var kok = belge.RootElement;

                bildirim.IyziReferenceCode = Metin(kok, "iyziReferenceCode")
                    // ⚠️ Referans kodu gelmezse tekrar eleme çalışmazdı;
                    // token+zaman ile yapay bir anahtar üretiyoruz.
                    ?? "yok-" + Guid.NewGuid().ToString("N")[..16];

                bildirim.OlayTipi = Metin(kok, "iyziEventType");
                bildirim.Token = Metin(kok, "token");
                bildirim.IyzicoPaymentId = Metin(kok, "paymentId");
                bildirim.Durum = Metin(kok, "status")
                    ?? Metin(kok, "paymentStatus");
            }
            catch
            {
                // Ayrıştırılamayan gövde de kayda geçiyor; sessizce
                // atmak "webhook hiç gelmedi" yanılgısı üretirdi.
                bildirim.IyziReferenceCode = "bozuk-" + Guid.NewGuid().ToString("N")[..16];
            }

            return bildirim;
        }

        private static string? Metin(System.Text.Json.JsonElement kok, string ad) =>
            kok.TryGetProperty(ad, out var deger) && deger.ValueKind
                == System.Text.Json.JsonValueKind.String
                ? deger.GetString()
                : null;

        // iyzico webhook imzası: HMAC-SHA256(gizliAnahtar,
        // gizliAnahtar + iyziEventType + paymentId + token + status)
        //
        // ⚠️ İmza GEÇMESE de bildirim işleniyor: doğrulama hesapta
        // açık olmayabiliyor ve sonucu zaten sağlayıcıya sorarak
        // öğreniyoruz. İmza burada bir güven göstergesi, kapı değil.
        private bool ImzaGecerliMi(IyzicoBildirimi bildirim, string govde)
        {
            try
            {
                using var belge = System.Text.Json.JsonDocument.Parse(govde);
                var gelen = Metin(belge.RootElement, "signature");

                if (string.IsNullOrWhiteSpace(gelen)
                    || string.IsNullOrWhiteSpace(_ayarlar.GizliAnahtar))
                {
                    return false;
                }

                var veri = _ayarlar.GizliAnahtar
                    + bildirim.OlayTipi
                    + bildirim.IyzicoPaymentId
                    + bildirim.Token
                    + bildirim.Durum;

                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_ayarlar.GizliAnahtar));
                var ozet = hmac.ComputeHash(Encoding.UTF8.GetBytes(veri));
                var beklenen = Convert.ToHexString(ozet).ToLowerInvariant();

                return CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(beklenen),
                    Encoding.UTF8.GetBytes(gelen.ToLowerInvariant()));
            }
            catch
            {
                return false;
            }
        }
    }


    public class OdemeBaslatDto
    {
        public int SiparisId { get; set; }
    }
}
