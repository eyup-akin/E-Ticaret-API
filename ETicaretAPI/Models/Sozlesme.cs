namespace ETicaretAPI.Models
{
    // ⭐ YENİ (Aşama 10) — SÖZLEŞME METNİ
    //
    // Metin koda gömülmüyor, SÜRÜMLENİYOR: metin değişince eski
    // onaylar "hangi metne verildi" bilgisini kaybetmesin.
    public class Sozlesme
    {
        public int Id { get; set; }

        // gizlilik / kullanim / mesafeli_satis / on_bilgilendirme
        public string Tip { get; set; } = string.Empty;

        // Tip içinde artan sürüm numarası.
        public int Surum { get; set; }

        public string Icerik { get; set; } = string.Empty;

        public DateTime YayinTarihi { get; set; } = DateTime.UtcNow;

        // ⚠️ Tip başına yalnızca BİR aktif sürüm (filtreli unique index).
        public bool AktifMi { get; set; } = true;
    }


    // ⭐ YENİ (Aşama 10) — ONAY KAYDI
    //
    // "Kim, hangi metnin hangi sürümünü, ne zaman onayladı."
    public class SozlesmeOnayi
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        // ⚠️ Sözleşmenin ID'si (yani SÜRÜMÜ) saklanıyor, tipi değil:
        // metin güncellenince eski onay eski metne bağlı kalmalı.
        public int SozlesmeId { get; set; }

        public DateTime OnayTarihi { get; set; } = DateTime.UtcNow;

        // ⚠️ İspat için tutuluyor; KVKK açısından tartışmalı ama
        // sözleşme onayında yaygın uygulama. Saklama süresi Aşama 11.
        public string? IpAdresi { get; set; }

        // Sipariş anında verilen onaylarda dolu.
        public int? OrderId { get; set; }
    }


    public static class SozlesmeTipi
    {
        public const string Gizlilik = "gizlilik";
        public const string Kullanim = "kullanim";
        public const string MesafeliSatis = "mesafeli_satis";
        public const string OnBilgilendirme = "on_bilgilendirme";

        // Kayıt sırasında onaylananlar.
        public static readonly string[] KayitSozlesmeleri = { Gizlilik, Kullanim };

        // Sipariş sırasında onaylananlar.
        public static readonly string[] SiparisSozlesmeleri = { MesafeliSatis, OnBilgilendirme };

        public static readonly string[] Gecerliler =
        {
            Gizlilik, Kullanim, MesafeliSatis, OnBilgilendirme
        };
    }
}
