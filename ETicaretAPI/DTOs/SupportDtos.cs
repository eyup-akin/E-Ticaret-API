using System.ComponentModel.DataAnnotations;

namespace ETicaretAPI.DTOs
{
    // ⭐ YENİ (Aşama 8) — DESTEK SİSTEMİ DTO'LARI

    // ---- YAZMA ----

    public class TalepOlusturDto
    {
        [Required(ErrorMessage = "Konu boş olamaz!")]
        [StringLength(150, MinimumLength = 3, ErrorMessage = "Konu 3-150 karakter olmalı!")]
        public string Konu { get; set; } = string.Empty;

        // ⚠️ Beyaz liste kontrolü DTO'da DEĞİL controller'da:
        // geçerli değerler `DestekKategorisi.Gecerliler` dizisinde ve
        // bir attribute'a sabit dizi yazmak, listeyi ikinci kez
        // tanımlamak olurdu.
        [Required(ErrorMessage = "Kategori seçmelisin!")]
        public string Kategori { get; set; } = string.Empty;

        // İlk mesaj talebin kendisiyle BİRLİKTE geliyor.
        //
        // ⚠️ Mesajsız talep açtırmıyoruz: "konu: kargo" yazıp
        // gönderen bir talep, admine hiçbir şey söylemez ve ilk iş
        // olarak "ne olmuş?" diye sormayı gerektirir.
        [Required(ErrorMessage = "Mesaj boş olamaz!")]
        [StringLength(2000, MinimumLength = 5, ErrorMessage = "Mesaj 5-2000 karakter olmalı!")]
        public string Mesaj { get; set; } = string.Empty;

        // ⚠️ Nullable: talep bir siparişe bağlı olmayabilir.
        // Sahiplik kontrolü ("bu sipariş gerçekten senin mi")
        // controller'da, sorgunun WHERE'inde yapılıyor.
        public int? OrderId { get; set; }
    }

    public class MesajEkleDto
    {
        [Required(ErrorMessage = "Mesaj boş olamaz!")]
        [StringLength(2000, MinimumLength = 1, ErrorMessage = "Mesaj en fazla 2000 karakter olabilir!")]
        public string Mesaj { get; set; } = string.Empty;
    }

    public class TalepDurumDto
    {
        [Required(ErrorMessage = "Durum boş olamaz!")]
        public string Durum { get; set; } = string.Empty;
    }


    // ---- OKUMA ----

    // Liste satırı. ⚠️ Yazışmanın tamamı YOK — "liste ucu ÖZET,
    // detay ucu TAM veri döndürür". Otuz talebin tüm mesajlarını
    // listede göndermek, hiç açılmayacak yazışmaları indirmek olurdu.
    public class TalepOzetDto
    {
        public int Id { get; set; }
        public string Konu { get; set; } = string.Empty;
        public string Kategori { get; set; } = string.Empty;
        public string Durum { get; set; } = string.Empty;
        public int? OrderId { get; set; }

        // Sipariş numarası — admin ve müşteri id değil bunu okuyor.
        // ⚠️ Talebe DONDURULMUYOR: canlı JOIN'den geliyor, çünkü
        // sipariş numarası hiç değişmiyor (Aşama 2'de unique index
        // ile sabitlendi).
        public string? SiparisNo { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public int MesajSayisi { get; set; }

        // ⚠️ Admin listesinde müşteri adı; müşteri listesinde null.
        // Aynı DTO iki tarafta kullanılıyor ve dolduran taraf karar
        // veriyor — müşteriye kendi adını göndermek gereksiz.
        public string? MusteriAdi { get; set; }
    }

    public class TalepMesajDto
    {
        public int Id { get; set; }
        public string Mesaj { get; set; } = string.Empty;
        public bool GonderenAdminMi { get; set; }

        // ⚠️ Ad CANLI okunuyor, dondurulmuyor: "bu kişi kim"
        // sorusunun cevabı bugüne ait (yorumcu adı kararının aynısı).
        public string GonderenAdi { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }

    public class TalepDetayDto
    {
        public int Id { get; set; }
        public string Konu { get; set; } = string.Empty;
        public string Kategori { get; set; } = string.Empty;
        public string Durum { get; set; } = string.Empty;

        public int? OrderId { get; set; }
        public string? SiparisNo { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public string? MusteriAdi { get; set; }
        public string? MusteriEposta { get; set; }

        public List<TalepMesajDto> Mesajlar { get; set; } = new();
    }
}
