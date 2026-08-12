namespace ETicaretAPI.Models
{
    // ⭐ YENİ (Aşama 9) — İADE TALEBİ
    //
    // Destek talebinden ayrı tablo: iadenin durum makinesi, parası ve
    // stok hareketi var; bunlar SupportTicket'ta boş duran kolonlar olurdu.
    public class ReturnRequest
    {
        public int Id { get; set; }

        public int OrderId { get; set; }

        // null = siparişin tamamı, dolu = yalnızca o kalem.
        // ⚠️ Kalemin TAMAMI iade ediliyor, adet seçilemiyor.
        public int? OrderItemId { get; set; }

        // Beyaz liste sunucuda doğrulanıyor (IadeSebebi.Gecerliler).
        public string Sebep { get; set; } = string.Empty;

        // Müşterinin kendi anlatımı; sebep etiket, bu ayrıntı.
        public string? Aciklama { get; set; }

        public string Durum { get; set; } = IadeDurumu.TalepEdildi;

        public DateTime TalepTarihi { get; set; } = DateTime.UtcNow;

        // Karar verilene kadar üçü de null.
        public DateTime? KararTarihi { get; set; }
        public int? KararVerenUserId { get; set; }
        public string? RedNedeni { get; set; }

        // ⚠️ Para gerçekten ödendiğinde donuyor. Hesap kuralı yarın
        // değişirse geçmiş iadelerin tutarı değişmesin.
        public decimal? IadeTutari { get; set; }

        // ⚠️ KararTarihi'nden ayrı: biri "ne zaman onayladık", bu
        // "ne zaman ödedik". Rapor buna göre dönem ayırıyor.
        public DateTime? ParaIadeTarihi { get; set; }
    }


    public static class IadeDurumu
    {
        public const string TalepEdildi = "talep_edildi";

        // ⚠️ "onaylandi" aynı zamanda "kargo bekleniyor" demek.
        // Plandaki ayrı `kargo_bekleniyor` durumu yazılmadı: ikisi aynı
        // gerçeği anlatıyordu (top müşteride).
        public const string Onaylandi = "onaylandi";

        public const string TeslimAlindi = "teslim_alindi";
        public const string ParaIadeEdildi = "para_iade_edildi";
        public const string Reddedildi = "reddedildi";

        // Kapanmamış talepler. Üç tüketicisi var: çakışma kontrolü,
        // uygunluk ucu ve dikkat paneli.
        public static readonly string[] Acikkalanlar =
        {
            TalepEdildi, Onaylandi, TeslimAlindi
        };
    }


    public static class IadeSebebi
    {
        public const string HataliUrun = "hatali_urun";
        public const string BedeneUymadi = "bedene_uymadi";
        public const string FarkliUrunGeldi = "farkli_urun_geldi";
        public const string HasarliGeldi = "hasarli_geldi";
        public const string Vazgectim = "vazgectim";
        public const string Diger = "diger";

        public static readonly string[] Gecerliler =
        {
            HataliUrun, BedeneUymadi, FarkliUrunGeldi,
            HasarliGeldi, Vazgectim, Diger
        };
    }
}
