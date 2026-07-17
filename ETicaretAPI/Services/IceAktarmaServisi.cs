using ETicaretAPI.Data;

namespace ETicaretAPI.Services
{
    // Excel içe aktarma iş mantığı burada olacak (7b/7c'de dolduracağız).
    // Şimdilik sadece 7a testi için uzun süren bir işi taklit ediyoruz.
    //
    // ÖNEMLİ: Bu servis Scoped kayıtlı ve AppDbContext'i constructor'dan alıyor.
    // Hangfire, bir işi çalıştırırken KENDİ scope'unu açıp bu servisi TAZE bir
    // DbContext ile üretir. Yani "kapanmış context" derdi yaşamayız —
    // Task.Run ile elle yapsaydık o sorunu yaşardık.
    public class IceAktarmaServisi
    {
        private readonly AppDbContext _context;

        public IceAktarmaServisi(AppDbContext context)
        {
            _context = context;
        }

        // 7a testi: gerçek Excel yok. Sadece 5 saniye "çalışıyormuş gibi" yapıp
        // ImportJob satırını adım adım güncelliyoruz ki dışarıdan izleyebilelim.
        public async Task TestIsiCalistir(int jobId)
        {
            var job = await _context.ImportJobs.FindAsync(jobId);
            if (job == null)
            {
                return;
            }

            job.Status = "Isleniyor";
            job.Total = 5;
            await _context.SaveChangesAsync();

            for (int i = 1; i <= 5; i++)
            {
                await Task.Delay(1000);   // uzun süren işi taklit et
                job.Success = i;
                await _context.SaveChangesAsync();
            }

            job.Status = "Tamamlandi";
            job.CompletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }
}