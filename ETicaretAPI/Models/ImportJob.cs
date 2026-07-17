namespace ETicaretAPI.Models
{
    // Bir Excel içe aktarma işinin durumunu ve sonucunu tutar.
    // Kullanıcıya "1180 başarılı / 70 başarısız, %60" gibi bilgiyi
    // buradan göstereceğiz. (Hangfire'ın kendi tabloları AYRI iştir;
    // bu tablo bizim kullanıcıya dönük durum kaydımız.)
    public class ImportJob
    {
        public int Id { get; set; }

        // Yüklenen dosyanın adı — kullanıcıya göstermek için
        public string FileName { get; set; } = string.Empty;

        // Bekliyor / Isleniyor / Tamamlandi / Hata
        public string Status { get; set; } = "Bekliyor";

        public int Total { get; set; }    // toplam satır
        public int Success { get; set; }  // başarılı
        public int Failed { get; set; }   // başarısız

        // İşi başlatan admin (kim yükledi bilelim). Zorunlu değil.
        public int? CreatedByUserId { get; set; }

        // İş hata ile biterse kısa açıklama
        public string? ErrorMessage { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }
}