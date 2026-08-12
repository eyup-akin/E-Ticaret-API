using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaretAPI.Migrations
{
    /// <inheritdoc />
    public partial class TelefonDefteri : Migration
    {
        // ⚠️⚠️ BU MIGRATION ELLE DÜZENLENDİ — ÜRETİLEN HALİ VERİ
        // KAYBEDİYORDU.
        //
        // EF'in ürettiği sıra şuydu: önce `Addresses.Phone` kolonunu
        // DÜŞÜR, sonra `Phones` tablosunu yarat. Yani numaraları
        // taşıyacak tablo, numaralar silindikten SONRA doğuyordu.
        // "Done" yazardı, hata vermezdi, veri giderdi.
        //
        // Doğru sıra: tabloyu yarat → PhoneId kolonunu ekle → veriyi
        // TAŞI → eski kolonu düşür.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- 1) Yeni tablo ----
            migrationBuilder.CreateTable(
                name: "Phones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Numara = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Etiket = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DogrulandiMi = table.Column<bool>(type: "bit", nullable: false),
                    VarsayilanMi = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Phones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Phones_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // ⚠️ Benzersizlik indeksi GERİ DOLDURMADAN ÖNCE kuruluyor
            // — bilinçli. Aşağıdaki INSERT bir şekilde mükerrer satır
            // üretirse migration burada PATLASIN istiyoruz. Sonra
            // kursaydık, hatalı veri sessizce yerleşir ve indeks
            // kurulamadığı için kural bir daha hiç işlemezdi.
            migrationBuilder.CreateIndex(
                name: "IX_Phones_UserId_Numara",
                table: "Phones",
                columns: new[] { "UserId", "Numara" },
                unique: true);

            // ---- 2) Adrese referans kolonu ----
            migrationBuilder.AddColumn<int>(
                name: "PhoneId",
                table: "Addresses",
                type: "int",
                nullable: true);

            // ---- 3) GERİ DOLDURMA ----
            //
            // ⚠️ NORMALİZASYON KURALI C# TARAFIYLA AYNI OLMALI
            // (TelefonBicimi.Normalize). Farklı olsalardı migration'ın
            // yazdığı satırlar, uygulamanın üretemeyeceği bir biçimde
            // dururdu — ve o fark ancak aylar sonra, "aynı numarayı
            // ikinci kez ekleyebiliyorum" diye fark edilirdi.
            //
            // ⚠️ 10 HANEYE İNMEYEN DEĞERLER TAŞINMIYOR, PhoneId null
            // kalıyor. Veritabanında bugün 19 haneli bir çöp kayıt var
            // (test girdisi). Onu kırpıp "geçerli" bir numaraya
            // çevirmek, olmayan bir numarayı UYDURMAK olurdu; kurye o
            // numarayı arardı. Müşteri sipariş verirken numara seçmesi
            // istenecek. "Yanlış sayı, eksik sayıdan tehlikelidir."
            //
            // ⚠️ DogrulandiMi = 0. Bu numaraların doğrulandığına dair
            // hiçbir kanıtımız yok. 1 yazmak, UnitCost ve OrderItem.VatRate
            // kararlarındaki hatanın aynısı olurdu.
            migrationBuilder.Sql(@"
-- Ham telefon metinlerini kanonik 10 haneye indir.
SELECT
    a.Id     AS AdresId,
    a.UserId AS UserId,
    CASE
        WHEN LEN(t.Rakamlar) = 14 AND LEFT(t.Rakamlar, 4) = '0090' THEN RIGHT(t.Rakamlar, 10)
        WHEN LEN(t.Rakamlar) = 13 AND LEFT(t.Rakamlar, 3) = '090'  THEN RIGHT(t.Rakamlar, 10)
        WHEN LEN(t.Rakamlar) = 12 AND LEFT(t.Rakamlar, 2) = '90'   THEN RIGHT(t.Rakamlar, 10)
        WHEN LEN(t.Rakamlar) = 11 AND LEFT(t.Rakamlar, 1) = '0'    THEN RIGHT(t.Rakamlar, 10)
        ELSE t.Rakamlar
    END AS Numara
INTO #Kanonik
FROM Addresses a
CROSS APPLY (
    SELECT REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
             LTRIM(RTRIM(a.Phone)),
             ' ', ''), '-', ''), '(', ''), ')', ''), '+', ''), '.', '') AS Rakamlar
) t
WHERE a.Phone IS NOT NULL AND LTRIM(RTRIM(a.Phone)) <> '';

-- Türkiye numarası olmayan / bozuk girdileri ele.
DELETE FROM #Kanonik WHERE LEN(Numara) <> 10 OR Numara LIKE '%[^0-9]%';

-- Kullanıcı bazında TEKİLLEŞTİREREK deftere taşı.
-- Her kullanıcının ilk numarası varsayılan olur.
INSERT INTO Phones (UserId, Numara, Etiket, DogrulandiMi, VarsayilanMi, CreatedAt)
SELECT
    d.UserId,
    d.Numara,
    N'Kayıtlı',
    0,
    CASE WHEN ROW_NUMBER() OVER (PARTITION BY d.UserId ORDER BY d.Numara) = 1
         THEN 1 ELSE 0 END,
    GETUTCDATE()
FROM (SELECT DISTINCT UserId, Numara FROM #Kanonik) d;

-- Adresleri yeni satırlara bağla.
UPDATE a
SET a.PhoneId = p.Id
FROM Addresses a
INNER JOIN #Kanonik k ON k.AdresId = a.Id
INNER JOIN Phones   p ON p.UserId  = k.UserId AND p.Numara = k.Numara;

DROP TABLE #Kanonik;
");

            // ---- 4) Eski kolon artık gereksiz ----
            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Addresses");

            // ---- 5) İlişki ----
            migrationBuilder.CreateIndex(
                name: "IX_Addresses_PhoneId",
                table: "Addresses",
                column: "PhoneId");

            migrationBuilder.AddForeignKey(
                name: "FK_Addresses_Phones_PhoneId",
                table: "Addresses",
                column: "PhoneId",
                principalTable: "Phones",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ⚠️ Down da elle düzenlendi: üretilen hali önce tabloyu
            // düşürüp SONRA Phone kolonunu ekliyordu — yani geri
            // dönülse bile bütün numaralar boş string olurdu.
            // Geri dönüş de veriyi taşımak zorunda.
            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Addresses",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            // Kanonik hali okunur biçime çevirerek geri yaz:
            // 5528083129 → 0552 808 31 29 (TelefonBicimi.Goster ile aynı)
            migrationBuilder.Sql(@"
UPDATE a
SET a.Phone = '0' + SUBSTRING(p.Numara, 1, 3) + ' '
                  + SUBSTRING(p.Numara, 4, 3) + ' '
                  + SUBSTRING(p.Numara, 7, 2) + ' '
                  + SUBSTRING(p.Numara, 9, 2)
FROM Addresses a
INNER JOIN Phones p ON p.Id = a.PhoneId
WHERE LEN(p.Numara) = 10;
");

            migrationBuilder.DropForeignKey(
                name: "FK_Addresses_Phones_PhoneId",
                table: "Addresses");

            migrationBuilder.DropIndex(
                name: "IX_Addresses_PhoneId",
                table: "Addresses");

            migrationBuilder.DropColumn(
                name: "PhoneId",
                table: "Addresses");

            migrationBuilder.DropTable(
                name: "Phones");
        }
    }
}
