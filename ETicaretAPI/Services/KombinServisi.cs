using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;

namespace ETicaretAPI.Services
{
    public class KombinDto
    {
        public int Id { get; set; }
        public string Ad { get; set; } = string.Empty;
        public string? Aciklama { get; set; }

        public List<KombinUrunDto> Urunler { get; set; } = new();

        public decimal NormalToplam { get; set; }
        public decimal KombinFiyati { get; set; }
        public decimal Tasarruf { get; set; }
        public int IndirimYuzdesi { get; set; }
    }

    public class KombinUrunDto
    {
        public int Id { get; set; }
        public string Ad { get; set; } = string.Empty;
        public decimal Fiyat { get; set; }
        public string? ResimUrl { get; set; }
    }


    // ⭐ YENİ — KOMBİNLER
    //
    // ⚠️ İki ayrı şey, iki ayrı bölüm:
    //   • Kombin        → admin tanımlar, GERÇEK indirimi var
    //   • Birlikte alınanlar → sipariş verisinden otomatik, indirim YOK
    // Karıştırılmıyorlar ki ekranda "tasarruf" yazısı yalnızca gerçek
    // bir indirim varken çıksın.
    public class KombinServisi
    {
        private readonly AppDbContext _context;

        public KombinServisi(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>Admin tanımlı kombinler (indirimli).</summary>
        public async Task<List<KombinDto>> UrunKombinleriAsync(int productId, int enFazla = 5)
        {
            var kombinIdler = await _context.KombinUrunler
                .Where(ku => ku.ProductId == productId)
                .Select(ku => ku.KombinId)
                .Distinct()
                .ToListAsync();

            var kombinler = await _context.Kombinler
                .Where(k => k.AktifMi && kombinIdler.Contains(k.Id))
                .OrderBy(k => k.Id)
                .Take(enFazla)
                .ToListAsync();

            var sonuc = new List<KombinDto>();

            foreach (var k in kombinler)
            {
                var urunler = await KombinUrunleriniGetirAsync(k.Id);

                // ⚠️ Ürünlerden biri pasif/silinmişse kombin hiç
                // gösterilmiyor: eksik bir seti satmak olmaz.
                if (urunler.Count < 2)
                {
                    continue;
                }

                var normal = urunler.Sum(u => u.Fiyat);
                var tasarruf = Math.Round(normal * k.IndirimYuzdesi / 100m, 2, MidpointRounding.AwayFromZero);

                sonuc.Add(new KombinDto
                {
                    Id = k.Id,
                    Ad = k.Ad,
                    Aciklama = k.Aciklama ?? string.Join(" + ", urunler.Select(u => u.Ad)),
                    Urunler = urunler,
                    NormalToplam = normal,
                    KombinFiyati = normal - tasarruf,
                    Tasarruf = tasarruf,
                    IndirimYuzdesi = k.IndirimYuzdesi
                });
            }

            return sonuc;
        }

        /// <summary>
        /// "Bunu alanlar bunu da aldı" — aynı siparişte geçen ürünler.
        /// ⚠️ İndirim yok, sadece öneri.
        /// </summary>
        public async Task<List<int>> BirlikteAlinanIdlerAsync(int productId, int enFazla = 10)
        {
            var siparisIdler = await _context.OrderItems
                .Where(oi => oi.ProductId == productId)
                .Select(oi => oi.OrderId)
                .Distinct()
                .ToListAsync();

            if (siparisIdler.Count == 0)
            {
                return new List<int>();
            }

            // ⚠️ İptal edilen siparişler sayılmıyor.
            return await _context.OrderItems
                .Where(oi => siparisIdler.Contains(oi.OrderId)
                          && oi.ProductId != productId
                          && _context.Orders.Any(o => o.Id == oi.OrderId
                                                   && o.Status != SiparisDurumlari.Iptal))
                .GroupBy(oi => oi.ProductId)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => g.Key)
                .Take(enFazla)
                .ToListAsync();
        }

        /// <summary>
        /// Sepetteki ürünlere uyan kombinlerin toplam indirimi.
        /// Kombinin TÜM ürünleri sepette olmalı.
        /// </summary>
        public async Task<decimal> SepetIndirimiAsync(List<int> sepetUrunIdleri)
        {
            if (sepetUrunIdleri.Count == 0)
            {
                return 0m;
            }

            var aktifler = await _context.Kombinler
                .Where(k => k.AktifMi)
                .Select(k => new
                {
                    k.Id,
                    k.IndirimYuzdesi,
                    urunler = _context.KombinUrunler
                        .Where(ku => ku.KombinId == k.Id)
                        .Select(ku => ku.ProductId)
                        .ToList()
                })
                .ToListAsync();

            decimal toplam = 0m;

            foreach (var k in aktifler)
            {
                if (k.urunler.Count < 2)
                {
                    continue;
                }

                // ⚠️ Kombinin TAMAMI sepette olmalı; parçası eksikken
                // indirim vermek paketi bozup ucuza almaya kapı açardı.
                if (!k.urunler.All(sepetUrunIdleri.Contains))
                {
                    continue;
                }

                var fiyatlar = await _context.Products
                    .Where(p => k.urunler.Contains(p.Id))
                    .SumAsync(p => (decimal?)p.Price) ?? 0m;

                toplam += Math.Round(fiyatlar * k.IndirimYuzdesi / 100m, 2, MidpointRounding.AwayFromZero);
            }

            return toplam;
        }

        private async Task<List<KombinUrunDto>> KombinUrunleriniGetirAsync(int kombinId)
        {
            var idler = await _context.KombinUrunler
                .Where(ku => ku.KombinId == kombinId)
                .Select(ku => ku.ProductId)
                .ToListAsync();

            return await UrunDtolariAsync(idler);
        }

        public async Task<List<KombinUrunDto>> UrunDtolariAsync(List<int> idler)
        {
            // Yalnızca satıştaki ürünler.
            var urunler = await _context.Products
                .Where(p => idler.Contains(p.Id) && p.IsActive && !p.ArsivlendiMi)
                .Select(p => new KombinUrunDto
                {
                    Id = p.Id,
                    Ad = p.Name,
                    Fiyat = p.Price,
                    ResimUrl = _context.ProductImages
                        .Where(r => r.ProductId == p.Id)
                        .OrderByDescending(r => r.IsMain)
                        .ThenBy(r => r.SortOrder)
                        .Select(r => r.Url)
                        .FirstOrDefault()
                })
                .ToListAsync();

            // Verilen sırayı koru.
            return idler
                .Select(id => urunler.FirstOrDefault(u => u.Id == id))
                .Where(u => u != null)
                .Select(u => u!)
                .ToList();
        }
    }
}
