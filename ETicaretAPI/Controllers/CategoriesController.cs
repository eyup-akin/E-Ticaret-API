using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.Models;
using ETicaretAPI.DTOs;
using ETicaretAPI.Services;   // ⭐ YENİ — denetim kaydı

namespace ETicaretAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CategoriesController : ControllerBase
    {
        private readonly AppDbContext _context;

        // ⭐ YENİ — denetim kaydı. Kategori değişikliği ürünlerin
        // müşteride nerede göründüğünü belirliyor; sessiz kalmamalı.
        private readonly DenetimKaydi _denetim;

        public CategoriesController(AppDbContext context, DenetimKaydi denetim)
        {
            _context = context;
            _denetim = denetim;
        }


        // Token'dan admin kimliği. Yazma uçları [Authorize] altında.
        private int AdminId()
        {
            var talep = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);

            return talep != null && int.TryParse(talep.Value, out var id) ? id : 0;
        }

        // 🟢 GET /api/categories
        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            // ⭐ DEĞİŞTİ (GV/Faz 5) — SAYAÇ ARTIK MÜŞTERİNİN GÖRDÜĞÜNÜ SAYIYOR.
            //
            // ⚠️ BU BİR HATA DÜZELTMESİYDİ VE ÖLÇÜLDÜ.
            //
            // Sayım hiçbir görünürlük koşulu içermiyordu: pasif ve
            // arşivli ürünler de sayılıyordu. Sonuç, ekranda
            // birbiriyle çelişen iki sayı:
            //
            //     "Elektronik (15)"  →  içeri girince 6 ürün
            //     Toplam 52 diyordu  →  müşteri 36 ürün görüyordu
            //
            // Bu, projede en çok kaçındığımız şeyin ta kendisi:
            // ekranda yazan sayının listeyle çelişmesi. Filtre
            // panelindeki sayaç için 28 senaryo yazmıştık, burada
            // aynı hata açıkta duruyordu.
            //
            // ⚠️ Koşullar ProductsController.UrunSorgusuKur'daki
            // müşteri dalıyla AYNI olmak zorunda. Orada değişirse
            // burası da değişmeli — üçüncü bir tüketici çıkarsa
            // koşul ortak bir yere taşınmalı.
            //
            // ⚠️ Bu uç ADMIN için de aynı sayıyı döndürüyor. Panelde
            // kategori başına "kaç ürün var" bilgisi bir yönetim
            // sayısı değil, gezinme yardımı; ayrıca admin ürün
            // listesinde gerçek sayıyı zaten görüyor. Rol ayrımı
            // eklemek, aynı ucun iki farklı sayı döndürmesi demekti.
            //
            // DİKKAT: Bunu tek sorguda yapıyoruz.
            // Yanlış yol: önce kategorileri çek, sonra her biri için ayrı COUNT sorgusu at
            // (5 kategori = 6 sorgu → "N+1 problemi"). SQL bunu tek seferde yapabilir.
            var categories = await _context.Categories
                .Select(c => new CategoryDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    ProductCount = _context.Products
                        .Count(p => p.CategoryId == c.Id && p.IsActive && !p.ArsivlendiMi)
                })
                .ToListAsync();

            return Ok(categories);
        }

        // 🟢 GET /api/categories/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetCategory(int id)
        {
            var category = await _context.Categories.FindAsync(id);

            if (category == null)
            {
                return NotFound(new { mesaj = "Kategori bulunamadı biladerim!" });
            }

            var urunSayisi = await _context.Products.CountAsync(p => p.CategoryId == id);

            return Ok(new CategoryDto
            {
                Id = category.Id,
                Name = category.Name,
                ProductCount = urunSayisi
            });
        }

        // 🔴 POST /api/categories — sadece admin
        [Authorize(Roles = "admin")]
        [HttpPost]
        public async Task<IActionResult> CreateCategory([FromBody] CategoryCreateDto dto)
        {
            // validation kontrolü
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var category = new Category { Name = dto.Name };

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();

            // ⭐ YENİ — DENETİM KAYDI. ⚠️ SaveChanges'ten sonra: Id lazım.
            await _denetim.EkleAsync(
                yapanId: AdminId(),
                hedefId: AdminId(),
                hedefAd: DenetimEtiketi.Kategori(category.Id, category.Name),
                islem: DenetimIslemi.KategoriEklendi,
                eski: null,
                yeni: DenetimDegeri.Yaz("ad", category.Name));

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Kategori eklendi biladerim!", id = category.Id });
        }

        // 🔴 PUT /api/categories/5 — sadece admin
        [Authorize(Roles = "admin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCategory(int id, [FromBody] CategoryCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var category = await _context.Categories.FindAsync(id);

            if (category == null)
            {
                return NotFound(new { mesaj = "Güncellenecek kategori bulunamadı!" });
            }

            var eskiAd = category.Name;
            category.Name = dto.Name;

            // ⚠️ Ad değişmediyse kayıt yok — aynı adı tekrar kaydetmek
            // bir değişiklik değil.
            if (eskiAd != category.Name)
            {
                await _denetim.EkleAsync(
                    yapanId: AdminId(),
                    hedefId: AdminId(),
                    hedefAd: DenetimEtiketi.Kategori(category.Id, category.Name),
                    islem: DenetimIslemi.KategoriGuncellendi,
                    eski: DenetimDegeri.Yaz("ad", eskiAd),
                    yeni: DenetimDegeri.Yaz("ad", category.Name));
            }

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Kategori güncellendi biladerim!" });
        }

        // 🔴 DELETE /api/categories/5 — sadece admin
        [Authorize(Roles = "admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCategory(int id)
        {
            var category = await _context.Categories.FindAsync(id);

            if (category == null)
            {
                return NotFound(new { mesaj = "Silinecek kategori zaten yok!" });
            }

            // ⭐ YENİ — İÇİ DOLU KATEGORİ SİLİNEMEZ
            // Bu kategoriye bağlı ürün varsa, SQL zaten foreign key hatası fırlatırdı.
            // Ama o hata teknik ve anlaşılmaz. Biz önden kontrol edip
            // admin'in anlayacağı bir dille söylüyoruz.
            var urunSayisi = await _context.Products.CountAsync(p => p.CategoryId == id);

            if (urunSayisi > 0)
            {
                return BadRequest(new
                {
                    mesaj = $"Bu kategoride {urunSayisi} ürün var. Önce ürünleri başka bir kategoriye taşı veya sil."
                });
            }

            _context.Categories.Remove(category);

            // ⭐ YENİ — DENETİM KAYDI, satır silinmeden önce.
            await _denetim.EkleAsync(
                yapanId: AdminId(),
                hedefId: AdminId(),
                hedefAd: DenetimEtiketi.Kategori(category.Id, category.Name),
                islem: DenetimIslemi.KategoriSilindi,
                eski: DenetimDegeri.Yaz("ad", category.Name),
                yeni: null);

            await _context.SaveChangesAsync();

            return Ok(new { mesaj = "Kategori silindi biladerim!" });
        }
    }
}