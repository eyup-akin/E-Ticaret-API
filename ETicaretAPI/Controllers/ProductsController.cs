using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;

namespace ETicaretAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env; // wwwroot'un yerini bilir

        // Resim yükleme kuralları — tek yerde dursun
        private const long MaxDosyaBoyutu = 5 * 1024 * 1024; // 5 MB
        private static readonly string[] IzinliUzantilar = { ".jpg", ".jpeg", ".png", ".webp" };
        private static readonly string[] IzinliTipler = { "image/jpeg", "image/png", "image/webp" };

        public ProductsController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // ==========================================================
        //  YARDIMCILAR
        // ==========================================================

        // wwwroot klasörünün diskteki tam yolu
        private string WebKok()
        {
            return string.IsNullOrEmpty(_env.WebRootPath)
                ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
                : _env.WebRootPath;
        }

        // Verilen ürün listesine resimlerini doldurur (tek sorguda — N+1 yok)
        private async Task ResimleriDoldur(List<ProductDto> urunler)
        {
            if (urunler.Count == 0)
            {
                return;
            }

            var idler = urunler.Select(u => u.Id).ToList();

            var resimler = await _context.ProductImages
                .Where(r => idler.Contains(r.ProductId))
                .OrderBy(r => r.SortOrder)
                .ToListAsync();

            foreach (var urun in urunler)
            {
                var kendiResimleri = resimler
                    .Where(r => r.ProductId == urun.Id)
                    .ToList();

                urun.Images = kendiResimleri
                    .Select(r => new ProductImageDto
                    {
                        Id = r.Id,
                        Url = r.Url,
                        IsMain = r.IsMain,
                        SortOrder = r.SortOrder
                    })
                    .ToList();

                // Ana resim varsa o, yoksa ilk resim, o da yoksa null
                var ana = kendiResimleri.FirstOrDefault(r => r.IsMain)
                          ?? kendiResimleri.FirstOrDefault();

                urun.MainImageUrl = ana?.Url;
            }
        }


        // Verilen ürün listesine puan özetini doldurur (tek sorguda — N+1 yok)
        private async Task PuanlariDoldur(List<ProductDto> urunler)
        {
            if (urunler.Count == 0)
            {
                return;
            }

            var idler = urunler.Select(u => u.Id).ToList();

            var puanlar = await _context.Reviews
                .Where(r => idler.Contains(r.ProductId))
                .GroupBy(r => r.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    Ortalama = g.Average(x => x.Rating),
                    Sayi = g.Count()
                })
                .ToListAsync();

            foreach (var urun in urunler)
            {
                var p = puanlar.FirstOrDefault(x => x.ProductId == urun.Id);
                if (p != null)
                {
                    urun.AverageRating = Math.Round(p.Ortalama, 1);
                    urun.ReviewCount = p.Sayi;
                }
            }
        }


        // Ürünlere favori sayısını doldurur (tek sorguda)
        private async Task FavorileriDoldur(List<ProductDto> urunler)
        {
            if (urunler.Count == 0)
            {
                return;
            }

            var idler = urunler.Select(u => u.Id).ToList();

            var sayilar = await _context.Favorites
                .Where(f => idler.Contains(f.ProductId))
                .GroupBy(f => f.ProductId)
                .Select(g => new { ProductId = g.Key, Sayi = g.Count() })
                .ToListAsync();

            foreach (var urun in urunler)
            {
                var s = sayilar.FirstOrDefault(x => x.ProductId == urun.Id);
                urun.FavoriteCount = s?.Sayi ?? 0;
            }
        }


        // Diskteki fiziksel dosyayı siler (yoksa sessizce geçer)
        private void DiskDosyasiniSil(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return;
            }

            // "/uploads/urunler/a.jpg" → "uploads\urunler\a.jpg"
            var goreliYol = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var tamYol = Path.Combine(WebKok(), goreliYol);

            if (System.IO.File.Exists(tamYol))
            {
                System.IO.File.Delete(tamYol);
            }
        }

        // GERÇEK KONTROL: dosyanın İÇİNE bak.
        // Uzantı ve ContentType istemciden gelir → yalan olabilir.
        // İlk byte'lar dosyanın kendisindedir → yalan söylenemez.
        private static async Task<bool> GercektenResimMi(IFormFile dosya)
        {
            using var akis = dosya.OpenReadStream();

            var baslik = new byte[12];
            var okunan = await akis.ReadAsync(baslik, 0, 12);

            if (okunan < 12)
            {
                return false; // 12 byte bile yoksa resim değildir
            }

            // JPEG:  FF D8 FF
            if (baslik[0] == 0xFF && baslik[1] == 0xD8 && baslik[2] == 0xFF)
            {
                return true;
            }

            // PNG:  89 50 4E 47 0D 0A 1A 0A
            if (baslik[0] == 0x89 && baslik[1] == 0x50 &&
                baslik[2] == 0x4E && baslik[3] == 0x47)
            {
                return true;
            }

            // WEBP:  "RIFF" ....  "WEBP"
            if (baslik[0] == 0x52 && baslik[1] == 0x49 &&
                baslik[2] == 0x46 && baslik[3] == 0x46 &&
                baslik[8] == 0x57 && baslik[9] == 0x45 &&
                baslik[10] == 0x42 && baslik[11] == 0x50)
            {
                return true;
            }

            return false;
        }



        // ==========================================================
        //  ÜRÜN ENDPOINT'LERİ
        // ==========================================================

        // 🟢 GET /api/products?categoryId=2&search=nike
        [HttpGet]
        public async Task<IActionResult> GetProducts(
            [FromQuery] int? categoryId,
            [FromQuery] string? search)
        {
            var query = _context.Products.AsQueryable();

            if (categoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == categoryId.Value);
            }

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(p => p.Name.Contains(search));
            }

            var products = await query
                .Select(p => new ProductDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Price = p.Price,
                    Stock = p.Stock,
                    CategoryId = p.CategoryId
                })
                .ToListAsync();

            await ResimleriDoldur(products);
            await PuanlariDoldur(products);

            return Ok(products);
        }

        // 🟢 GET /api/products/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(int id)
        {
            var product = await _context.Products.FindAsync(id);

            if (product == null)
            {
                return NotFound(new { mesaj = "Ürün bulunamadı biladerim!" });
            }

            var dto = new ProductDto
            {
                Id = product.Id,
                Name = product.Name,
                Price = product.Price,
                Stock = product.Stock,
                CategoryId = product.CategoryId
            };

            await ResimleriDoldur(new List<ProductDto> { dto });
            await PuanlariDoldur(new List<ProductDto> { dto });
            await FavorileriDoldur(new List<ProductDto> { dto });


            return Ok(dto);
        }

        // 🔴 POST /api/products
        [Authorize(Roles = "admin")]
        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] ProductCreateDto dto)
        {
            var product = new Product
            {
                Name = dto.Name,
                Price = dto.Price,
                Stock = dto.Stock,
                CategoryId = dto.CategoryId
            };

            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            // id'yi döndürüyoruz — panel bunu alıp hemen resim yükleyecek
            return Ok(new { mesaj = "Ürün eklendi biladerim!", id = product.Id });
        }

        // 🔴 PUT /api/products/5
        [Authorize(Roles = "admin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(int id, [FromBody] ProductCreateDto dto)
        {
            var product = await _context.Products.FindAsync(id);

            if (product == null)
            {
                return NotFound(new { mesaj = "Güncellenecek ürün bulunamadı!" });
            }

            product.Name = dto.Name;
            product.Price = dto.Price;
            product.Stock = dto.Stock;
            product.CategoryId = dto.CategoryId;

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Ürün güncellendi biladerim!", id = product.Id });
        }

        // 🔴 DELETE /api/products/5
        [Authorize(Roles = "admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            var product = await _context.Products.FindAsync(id);

            if (product == null)
            {
                return NotFound(new { mesaj = "Silinecek ürün zaten yok!" });
            }

            // Ürünün resimlerini hem diskten hem veritabanından temizle
            var resimler = await _context.ProductImages
                .Where(r => r.ProductId == id)
                .ToListAsync();

            foreach (var resim in resimler)
            {
                DiskDosyasiniSil(resim.Url);
            }

            _context.ProductImages.RemoveRange(resimler);
            _context.Products.Remove(product);

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Ürün silindi biladerim!" });
        }

        // ==========================================================
        //  RESİM ENDPOINT'LERİ
        // ==========================================================

        // 🔴 POST /api/products/5/images   (multipart/form-data, alan adı: dosya)
        [Authorize(Roles = "admin")]
        [HttpPost("{id}/images")]
        public async Task<IActionResult> UploadImage(int id, [FromForm] IFormFile dosya)
        {
            // 1) Ürün var mı?
            var urunVarMi = await _context.Products.AnyAsync(p => p.Id == id);

            if (!urunVarMi)
            {
                return NotFound(new { mesaj = "Ürün bulunamadı biladerim!" });
            }

            // 2) Dosya geldi mi?
            if (dosya == null || dosya.Length == 0)
            {
                return BadRequest(new { mesaj = "Dosya seçilmedi!" });
            }

            // 3) Boyut kontrolü
            if (dosya.Length > MaxDosyaBoyutu)
            {
                return BadRequest(new { mesaj = "Dosya en fazla 5 MB olabilir!" });
            }

            // 4) Uzantı kontrolü — kullanıcı .exe yüklemesin
            var uzanti = Path.GetExtension(dosya.FileName).ToLowerInvariant();

            if (!IzinliUzantilar.Contains(uzanti))
            {
                return BadRequest(new { mesaj = "Sadece jpg, jpeg, png ve webp yüklenebilir!" });
            }

            // 5) İçerik tipi kontrolü — uzantıyı değiştirip kandırmasın
            if (!IzinliTipler.Contains(dosya.ContentType.ToLowerInvariant()))
            {
                return BadRequest(new { mesaj = "Geçersiz dosya tipi!" });
            }

            // 5.5) İÇERİK kontrolü — uzantı ve ContentType yalan söyleyebilir, byte'lar söyleyemez
            if (!await GercektenResimMi(dosya))
            {
                return BadRequest(new { mesaj = "Dosya gerçek bir resim değil!" });
            }

            // 6) Klasörü hazırla
            var klasor = Path.Combine(WebKok(), "uploads", "urunler");
            Directory.CreateDirectory(klasor); // varsa dokunmaz

            // 7) BENZERSİZ isim üret.
            //    Kullanıcının gönderdiği ismi ASLA kullanma:
            //    aynı isimli dosya üzerine yazar + "../../" gibi yol saldırısı riski var.
            var yeniAd = Guid.NewGuid().ToString("N") + uzanti;
            var tamYol = Path.Combine(klasor, yeniAd);

            // 8) Diske yaz
            using (var akis = new FileStream(tamYol, FileMode.Create))
            {
                await dosya.CopyToAsync(akis);
            }

            // 9) Veritabanına kaydet
            var mevcutSayi = await _context.ProductImages.CountAsync(r => r.ProductId == id);

            var resim = new ProductImage
            {
                ProductId = id,
                Url = "/uploads/urunler/" + yeniAd,
                IsMain = mevcutSayi == 0,   // ilk yüklenen otomatik ana resim olsun
                SortOrder = mevcutSayi
            };

            _context.ProductImages.Add(resim);
            await _context.SaveChangesAsync();

            return Ok(new ProductImageDto
            {
                Id = resim.Id,
                Url = resim.Url,
                IsMain = resim.IsMain,
                SortOrder = resim.SortOrder
            });
        }

        // 🔴 DELETE /api/products/images/12
        [Authorize(Roles = "admin")]
        [HttpDelete("images/{imageId}")]
        public async Task<IActionResult> DeleteImage(int imageId)
        {
            var resim = await _context.ProductImages.FindAsync(imageId);

            if (resim == null)
            {
                return NotFound(new { mesaj = "Resim bulunamadı!" });
            }

            var anaMiydi = resim.IsMain;
            var urunId = resim.ProductId;

            DiskDosyasiniSil(resim.Url);

            _context.ProductImages.Remove(resim);
            await _context.SaveChangesAsync();

            // Silinen ana resimse, kalanlardan ilkini ana yap (ürün resimsiz kalmasın)
            if (anaMiydi)
            {
                var kalan = await _context.ProductImages
                    .Where(r => r.ProductId == urunId)
                    .OrderBy(r => r.SortOrder)
                    .FirstOrDefaultAsync();

                if (kalan != null)
                {
                    kalan.IsMain = true;
                    await _context.SaveChangesAsync();
                }
            }

            return Ok(new { mesaj = "Resim silindi biladerim!" });
        }

        // 🔴 PUT /api/products/images/12/main — bu resmi ana resim yap
        [Authorize(Roles = "admin")]
        [HttpPut("images/{imageId}/main")]
        public async Task<IActionResult> SetMainImage(int imageId)
        {
            var resim = await _context.ProductImages.FindAsync(imageId);

            if (resim == null)
            {
                return NotFound(new { mesaj = "Resim bulunamadı!" });
            }

            // Aynı ürünün diğer resimlerinin ana işaretini kaldır
            var digerleri = await _context.ProductImages
                .Where(r => r.ProductId == resim.ProductId)
                .ToListAsync();

            foreach (var r in digerleri)
            {
                r.IsMain = (r.Id == imageId);
            }

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Ana resim güncellendi biladerim!" });
        }
    }
}