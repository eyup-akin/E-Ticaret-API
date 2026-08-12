using Microsoft.EntityFrameworkCore;
using ETicaretAPI.Data;
using ETicaretAPI.DTOs;

namespace ETicaretAPI.Services
{
    // ⭐ YENİ (Aşama 8) — YAZIŞMA OKUMA, TEK YERDE
    //
    // ⚠️ NEDEN SERVİS?
    // Aynı sorgunun İKİ tüketicisi var: müşteri talep detayı
    // (`SupportController`) ve admin talep detayı
    // (`AdminSupportController`). İkisinde ayrı ayrı yazsaydık —
    // ki ilk yazımda öyleydi — sıralama ya da gönderen adının
    // nereden okunduğu birinde değişince diğeri sessizce eski
    // davranışta kalırdı: müşteri ile adminin AYNI konuşmayı farklı
    // sırada görmesi.
    //
    // "Kural tek yerde kullanılıyorsa orada durur; ikinci tüketici
    // çıktığı an ortak yere taşınır."
    //
    // ⚠️ Sınıf yazma yapmıyor, yalnızca okuyor. Mesaj EKLEME iki
    // tarafta gerçekten farklı (müşteri talebi yeniden açıyor, admin
    // "yanıtlandı" yapıyor) ve ortaklaştırmak o farkı bir
    // `bool adminMi` parametresinin arkasına saklamak olurdu.
    public class DestekYazismasi
    {
        private readonly AppDbContext _context;

        public DestekYazismasi(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<TalepMesajDto>> MesajlariGetirAsync(int ticketId)
        {
            // ⚠️ Gönderen adı CANLI JOIN'den — dondurulmuyor.
            // "Bu kişi kim" sorusunun cevabı bugüne ait; adını
            // değiştiren kişi her yerde yeni adıyla görünmeli
            // (yorumcu adı kararının aynısı).
            //
            // ⚠️ Buna karşılık `GonderenAdminMi` DONDURULMUŞ bir
            // alan: rol değişince geçmiş yazışmanın tarafları yer
            // değiştirmesin diye. Gerekçesi modelde yazılı.
            return await _context.SupportMessages
                .Where(m => m.TicketId == ticketId)
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Id)      // aynı saniyeye düşen iki mesaj için
                .Select(m => new TalepMesajDto
                {
                    Id = m.Id,
                    Mesaj = m.Mesaj,
                    GonderenAdminMi = m.GonderenAdminMi,
                    GonderenAdi = _context.Users
                        .Where(u => u.Id == m.GonderenUserId)
                        .Select(u => u.FullName)
                        .FirstOrDefault() ?? "Bilinmeyen",
                    CreatedAt = m.CreatedAt
                })
                .ToListAsync();
        }
    }
}
