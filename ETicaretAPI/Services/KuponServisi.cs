using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;

namespace ETicaretAPI.Services
{
    // Kupon doğrulama sonucunu taşıyan basit sınıf.
    // Hem "geçerli mi" hem "neden geçersiz" hem "ne kadar indirim" döner.
    public class KuponSonucu
    {
        public bool Gecerli { get; set; }
        public string Mesaj { get; set; } = string.Empty;
        public decimal IndirimTutari { get; set; }
        public Coupon? Kupon { get; set; }
    }

    // Sepetteki bir kalemin kupon hesabı için gereken bilgisi.
    // Product entity'sinin tamamını taşımıyoruz — sadece lazım olanlar.
    public class SepetKalemi
    {
        public int ProductId { get; set; }
        public int CategoryId { get; set; }
        public int Adet { get; set; }
        public decimal BirimFiyat { get; set; }
    }

    // KUPON MANTIĞI TEK YERDE
    //
    // Neden ayrı servis?
    //   Bu hesap İKİ yerden çağrılıyor:
    //     1) Müşteri sepette kupon girince ("ne kadar inecek?")
    //     2) Sipariş oluşurken (gerçek uygulama)
    //   İkisinde ayrı yazsaydık biri güncellenip diğeri unutulurdu ve
    //   gösterilen indirimle uygulanan indirim FARKLI olurdu.
    public class KuponServisi
    {
        private readonly AppDbContext _context;

        public KuponServisi(AppDbContext context)
        {
            _context = context;
        }

        // Kuponu baştan sona doğrular ve indirim tutarını hesaplar.
        //
        // ⚠️ İndirim tutarı DIŞARIDAN ALINMAZ, burada hesaplanır.
        // Mobil "500 TL indirim var" dese bile ona inanmıyoruz.
        public async Task<KuponSonucu> DogrulaAsync(
            string kod,
            int userId,
            List<SepetKalemi> sepet)
        {
            // ---------- 1) KOD BOŞ MU ----------
            if (string.IsNullOrWhiteSpace(kod))
            {
                return Gecersiz("Kupon kodu boş olamaz.");
            }

            // Kodu normalize et: baş/son boşluk sil, BÜYÜK harfe çevir.
            // Kullanıcı "indirim10 " yazsa da "INDIRIM10" ile eşleşsin.
            var temizKod = kod.Trim().ToUpperInvariant();

            // ---------- 2) KUPON VAR MI ----------
            var kupon = await _context.Coupons
                .FirstOrDefaultAsync(c => c.Code == temizKod);

            if (kupon == null)
            {
                return Gecersiz("Böyle bir kupon yok.");
            }

            // ---------- 3) AKTİF Mİ ----------
            if (!kupon.IsActive)
            {
                return Gecersiz("Bu kupon şu anda kullanılamıyor.");
            }

            // ---------- 4) TARİH ARALIĞINDA MI ----------
            var simdi = DateTime.UtcNow;

            if (simdi < kupon.StartsAt)
            {
                return Gecersiz("Bu kupon henüz başlamadı.");
            }

            if (simdi > kupon.EndsAt)
            {
                return Gecersiz("Bu kuponun süresi dolmuş.");
            }


            // ══════════════════════════════════════════════════════════
            // ⚠️ AŞAĞIDAKİ İKİ KONTROL (5 ve 6) "TAVSİYE NİTELİĞİNDE"
            //
            // Bu iki kontrol yarış koşuluna AÇIKTIR: okuma ile kullanım
            // arasında başka bir istek araya girebilir.
            //
            // İki farklı yerden çağrılıyoruz ve anlamları farklı:
            //
            //   1) POST /api/coupons/dogrula (sepette önizleme)
            //      → Burada tek kontrol bunlar. Sorun değil, çünkü ortada
            //        henüz bir sipariş yok; sadece "şu an durum ne" diyoruz.
            //        Kullanıcı sipariş verene kadar durum değişebilir,
            //        değişirse sipariş anında reddedilir.
            //
            //   2) POST /api/orders (gerçek sipariş)
            //      → Burada BAĞLAYICI DEĞİL, sadece hızlı yol. Bağlayıcı
            //        kontrol OrdersController'da, kupon satırı kilitliyken
            //        yapılıyor (ExecuteUpdateAsync + kilit altında sayım).
            //
            // Yani desen: "iyimser yol + kalkan". Buradaki kontroller %99
            // durumu ucuza eler ve nazik mesaj verir; gerçek koruma ise
            // sipariş akışındaki atomik işlemlerdedir.
            // ══════════════════════════════════════════════════════════
            // ---------- 5) TOPLAM KULLANIM LİMİTİ ----------
            if (kupon.UsageLimit.HasValue && kupon.UsedCount >= kupon.UsageLimit.Value)
            {
                return Gecersiz("Bu kuponun kullanım hakkı dolmuş.");
            }

            // ---------- 6) KİŞİ BAŞI LİMİT ----------
            var kullanimSayisi = await _context.CouponUsages
                .CountAsync(cu => cu.CouponId == kupon.Id && cu.UserId == userId);

            if (kullanimSayisi >= kupon.UsageLimitPerUser)
            {
                return Gecersiz("Bu kuponu daha önce kullandın.");
            }

            // ---------- 7) İNDİRİMİN UYGULANACAĞI TUTAR ----------
            // Kupon bir kategoriye bağlıysa SADECE o kategorideki ürünlerin
            // toplamı üzerinden indirim yapılır. Bağlı değilse tüm sepet.
            decimal indirimeEsasTutar;

            if (kupon.CategoryId.HasValue)
            {
                indirimeEsasTutar = sepet
                    .Where(k => k.CategoryId == kupon.CategoryId.Value)
                    .Sum(k => k.BirimFiyat * k.Adet);

                if (indirimeEsasTutar <= 0)
                {
                    return Gecersiz("Bu kupon sepetindeki ürünlerde geçerli değil.");
                }
            }
            else
            {
                indirimeEsasTutar = sepet.Sum(k => k.BirimFiyat * k.Adet);
            }

            // ---------- 8) MİNİMUM SEPET TUTARI ----------
            // Not: minimum kontrolü SEPETİN TAMAMINA bakar, kategori
            // filtresine değil. "200 TL üzeri alışverişte geçerli" derken
            // kastedilen toplam alışveriştir.
            var sepetToplami = sepet.Sum(k => k.BirimFiyat * k.Adet);

            if (kupon.MinOrderAmount > 0 && sepetToplami < kupon.MinOrderAmount)
            {
                return Gecersiz(
                    $"Bu kupon en az {kupon.MinOrderAmount:N2} TL'lik sepette geçerli.");
            }

            // ---------- 9) İNDİRİMİ HESAPLA ----------
            decimal indirim;

            if (kupon.DiscountType == "yuzde")
            {
                indirim = indirimeEsasTutar * kupon.DiscountValue / 100m;

                // Tavan varsa aşma. Örn: %20 ama en fazla 200 TL.
                if (kupon.MaxDiscountAmount.HasValue && indirim > kupon.MaxDiscountAmount.Value)
                {
                    indirim = kupon.MaxDiscountAmount.Value;
                }
            }
            else // "tutar"
            {
                indirim = kupon.DiscountValue;
            }

            // ⚠️ İNDİRİM SEPETTEN BÜYÜK OLAMAZ.
            // 50 TL'lik sepette 100 TL'lik kupon kullanılırsa toplam
            // EKSİ çıkar — yani müşteriye para vermiş oluruz.
            if (indirim > sepetToplami)
            {
                indirim = sepetToplami;
            }

            // Kuruş hassasiyetine yuvarla (MidpointRounding.AwayFromZero:
            // 0.005 → 0.01, bankacılık yuvarlaması değil, müşteri lehine)
            indirim = Math.Round(indirim, 2, MidpointRounding.AwayFromZero);

            return new KuponSonucu
            {
                Gecerli = true,
                Mesaj = "Kupon uygulandı.",
                IndirimTutari = indirim,
                Kupon = kupon
            };
        }

        // Kısa yardımcı — tekrar eden "geçersiz" nesnesini kurar
        private static KuponSonucu Gecersiz(string mesaj)
        {
            return new KuponSonucu
            {
                Gecerli = false,
                Mesaj = mesaj,
                IndirimTutari = 0,
                Kupon = null
            };
        }
    }
}