using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETicaretAPI.Support
{
    // ============================================================
    // TARİHLERİ HER ZAMAN UTC OLARAK YAZ / OKU
    //
    // SORUN:
    //   Veritabanına DateTime.UtcNow ile UTC yazıyoruz. Ama SQL
    //   Server'ın datetime2 kolonu saat dilimi bilgisi TUTMAZ.
    //   EF Core geri okurken Kind = Unspecified oluyor ve JSON'a
    //   "2026-07-29T13:00:00" diye, sonunda Z OLMADAN yazılıyor.
    //
    //   JavaScript standardına göre saat dilimi eki olmayan metin
    //   YEREL saat kabul edilir. Yani tarayıcı UTC 13:00'ü Türkiye
    //   saati 13:00 sanıyor — gerçekte 16:00 olması gerekirken.
    //   Sonuç: tüm ekranlarda 3 saat geri gösterim.
    //
    // ÇÖZÜM:
    //   Serileştirme katmanında müdahale ediyoruz. API'nin dış
    //   dünyayla konuştuğu TEK yer burası, o yüzden tek düzeltme
    //   tüm endpoint'leri kapsıyor.
    // ============================================================


    // İki dönüştürücünün ortak mantığı burada — kopyalamamak için.
    internal static class UtcYardimci
    {
        // Kind ne olursa olsun sonucu UTC yap.
        public static DateTime UtcYap(DateTime deger)
        {
            return deger.Kind switch
            {
                // Zaten UTC → dokunma
                DateTimeKind.Utc => deger,

                // Yerel saat → UTC'ye çevir (saat değeri değişir)
                DateTimeKind.Local => deger.ToUniversalTime(),

                // Belirsiz → "bu zaten UTC'ydi" diye etiketle.
                // Bu varsayımı yapabiliyoruz çünkü projede DateTime.Now
                // HİÇ kullanılmıyor, her yerde DateTime.UtcNow var.
                // Yani veritabanındaki her tarih UTC.
                _ => DateTime.SpecifyKind(deger, DateTimeKind.Utc)
            };
        }

        // Gelen metni UTC DateTime'a çevir.
        //
        // AssumeUniversal    : saat dilimi eki yoksa "UTC'dir" kabul et
        // AdjustToUniversal  : ek varsa (örn +03:00) UTC'ye çevir
        //
        // Böylece üç biçim de doğru okunur:
        //   "2026-08-01T14:30:00Z"       → 14:30 UTC
        //   "2026-08-01T17:30:00+03:00"  → 14:30 UTC
        //   "2026-08-01T14:30:00"        → 14:30 UTC
        public static DateTime Oku(string? metin)
        {
            if (string.IsNullOrWhiteSpace(metin))
            {
                throw new JsonException("Tarih alanı boş gönderilemez.");
            }

            var basarili = DateTime.TryParse(
                metin,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var tarih);

            if (!basarili)
            {
                // Kendi mesajımızı veriyoruz. Varsayılan hata mesajı
                // "The JSON value could not be converted to System.DateTime"
                // gibi bir şey olurdu ve hangi alan olduğu belli olmazdı.
                throw new JsonException($"Geçersiz tarih biçimi: '{metin}'");
            }

            return tarih;
        }

        // Kullandığımız çıktı biçimi: 2026-07-29T13:00:00.000Z
        //
        // Neden "O" (round-trip) biçimini kullanmıyoruz?
        //   "O" 7 haneli kesir üretir (13:00:00.0000000Z). JavaScript'in
        //   ISO ayrıştırıcısı standartta 3 haneye kadar tanımlı; çoğu
        //   tarayıcı fazlasını da kabul ediyor ama garanti değil.
        //   3 hane hem standart hem yeterli.
        public const string Bicim = "yyyy-MM-ddTHH:mm:ss.fffZ";
    }


    // ---------- DateTime (zorunlu alanlar) ----------
    public class UtcTarihDonusturucu : JsonConverter<DateTime>
    {
        public override DateTime Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return UtcYardimci.Oku(reader.GetString());
        }

        public override void Write(
            Utf8JsonWriter writer,
            DateTime value,
            JsonSerializerOptions options)
        {
            var utc = UtcYardimci.UtcYap(value);

            writer.WriteStringValue(
                utc.ToString(UtcYardimci.Bicim, CultureInfo.InvariantCulture));
        }
    }


    // ---------- DateTime? (null olabilen alanlar) ----------
    //
    // Ayrı bir sınıf gerekiyor mu? .NET'in çoğu sürümünde
    // System.Text.Json, DateTime? için otomatik olarak DateTime
    // dönüştürücüsünü sarmalıyor. Ama bu davranış sürüme göre
    // değişebiliyor ve projede CancelledAt, PaidAt, LockoutEnd gibi
    // bir sürü null olabilen tarih var. Açıkça yazmak garantiye alıyor —
    // "çalışıyor gibi görünüyor" ile "çalıştığını biliyorum" farkı.
    public class UtcTarihDonusturucuNullable : JsonConverter<DateTime?>
    {
        public override DateTime? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            return UtcYardimci.Oku(reader.GetString());
        }

        public override void Write(
            Utf8JsonWriter writer,
            DateTime? value,
            JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            var utc = UtcYardimci.UtcYap(value.Value);

            writer.WriteStringValue(
                utc.ToString(UtcYardimci.Bicim, CultureInfo.InvariantCulture));
        }
    }
}