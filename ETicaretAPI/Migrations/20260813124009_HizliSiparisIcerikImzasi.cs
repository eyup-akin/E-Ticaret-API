using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaretAPI.Migrations
{
    /// <inheritdoc />
    public partial class HizliSiparisIcerikImzasi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IcerikImzasi",
                table: "HizliSiparisler",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // ============================================================
            //  ⭐ ELLE EKLENDİ — GERİ DOLDURMA
            //
            //  ⚠️ BU BLOK OLMADAN MIGRATION PATLAR.
            //
            //  EF mevcut satırlara defaultValue olarak "" yazıyor. Aynı
            //  kullanıcının birden fazla kaydı varsa hepsi "" olur ve
            //  aşağıdaki BENZERSİZ indeks oluşturulamaz.
            //
            //  ⚠️ İMZA TARİFİ HizliSiparislerController.ImzaUret İLE
            //  BİREBİR AYNI OLMAK ZORUNDA:
            //    • ProductId'ye göre GRUPLA, adetleri TOPLA
            //      (aynı ürün siparişte iki kalem olabiliyor)
            //    • ProductId'ye göre ARTAN sırala
            //    • "id x adet" parçalarını "|" ile birleştir
            //    • UTF-8 baytlarının SHA-256'sı, KÜÇÜK harf hex
            //
            //  ⚠️ CAST(... AS varchar) ŞART, nvarchar DEĞİL.
            //  HASHBYTES bayt üzerinden çalışıyor; nvarchar her karakteri
            //  2 bayt yazar ve C#'ın UTF-8'inden farklı bir hash çıkardı.
            //  İmza yalnızca rakam, 'x' ve '|' içeriyor — hepsi ASCII,
            //  yani varchar baytları UTF-8 ile aynı.
            //  (İki tarafın aynı sonucu verdiği elle doğrulandı.)
            // ============================================================
            migrationBuilder.Sql(@"
                UPDATE h
                SET h.IcerikImzasi =
                    LOWER(CONVERT(varchar(64),
                        HASHBYTES('SHA2_256', CAST(x.imza AS varchar(max))), 2))
                FROM HizliSiparisler h
                CROSS APPLY (
                    SELECT STRING_AGG(
                               CAST(t.ProductId AS varchar(20)) + 'x' +
                               CAST(t.Adet AS varchar(20)), '|')
                           WITHIN GROUP (ORDER BY t.ProductId) AS imza
                    FROM (
                        SELECT oi.ProductId, SUM(oi.Quantity) AS Adet
                        FROM OrderItems oi
                        WHERE oi.OrderId = h.OrderId
                        GROUP BY oi.ProductId
                    ) t
                ) x
                WHERE x.imza IS NOT NULL;
            ");

            // ============================================================
            //  ⭐ ELLE EKLENDİ — MÜKERRER İÇERİKLERİ TEMİZLE
            //
            //  ⚠️ BU BLOK VERİ SİLİYOR.
            //
            //  Benzersiz indeks, aynı kullanıcıda aynı imzadan iki satır
            //  varken kurulamaz. Bu satırlar tam olarak düzeltmeye
            //  çalıştığımız hatanın kendisi: aynı içerikte ikinci bir
            //  hızlı sipariş kaydı.
            //
            //  ⚠️ EN ESKİSİ KALIYOR (CreatedAt, sonra Id). Müşterinin
            //  ilk kaydettiği niyet korunuyor; sonradan oluşan kopyalar
            //  gidiyor. En yenisini tutmak, ilk kaydın tarihini sessizce
            //  değiştirmek olurdu.
            //
            //  ⚠️ SİPARİŞLER SİLİNMİYOR — yalnızca "kaydettim" işareti.
            //  Siparişin kendisi ticari kayıt ve yerinde duruyor.
            // ============================================================
            migrationBuilder.Sql(@"
                WITH sirali AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (
                               PARTITION BY UserId, IcerikImzasi
                               ORDER BY CreatedAt, Id
                           ) AS sira
                    FROM HizliSiparisler
                )
                DELETE FROM HizliSiparisler
                WHERE Id IN (SELECT Id FROM sirali WHERE sira > 1);
            ");

            migrationBuilder.CreateIndex(
                name: "IX_HizliSiparisler_UserId_IcerikImzasi",
                table: "HizliSiparisler",
                columns: new[] { "UserId", "IcerikImzasi" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HizliSiparisler_UserId_IcerikImzasi",
                table: "HizliSiparisler");

            migrationBuilder.DropColumn(
                name: "IcerikImzasi",
                table: "HizliSiparisler");
        }
    }
}
