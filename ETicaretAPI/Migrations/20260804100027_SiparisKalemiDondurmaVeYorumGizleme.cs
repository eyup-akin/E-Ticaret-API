using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaretAPI.Migrations
{
    /// <inheritdoc />
    public partial class SiparisKalemiDondurmaVeYorumGizleme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "Reviews",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ProductName",
                table: "OrderItems",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "OrderItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);


            // ============================================================
            //  ⭐ BACKFILL — eski satırlara ne yazılacak?
            //
            //  EF yeni kolonu ekler ve varsayılan değeri yazar; ama "eski
            //  kayıtlarda gerçekte ne olmalıydı" sorusunu bilemez. Onu biz
            //  yazmak zorundayız.
            //
            //  Bu SQL, migration'ın PARÇASIDIR: Update-Database çalıştığında
            //  kolon ekleme ile birlikte tek transaction içinde çalışır.
            //  SSMS'te elle yapsaydık başka bir makinede veritabanı kurulunca
            //  bu adım atlanır ve kimse fark etmezdi.
            // ============================================================

            // 1) Ürün adlarını mevcut ürünlerden kopyala.
            //
            //    Neden UPDATE ... FROM ... JOIN?
            //    Standart UPDATE tek tabloya yazar; değeri BAŞKA bir tablodan
            //    alacaksak join gerekiyor. SQL Server'ın bu sözdizimi
            //    "oi takma adıyla OrderItems'a yaz, değeri join'lenmiş
            //    Products'tan al" demek.
            //
            //    LEFT(p.Name, 200) neden?
            //    Kolonu nvarchar(200) yaptık. Veritabanında 200 karakterden
            //    uzun bir ürün adı varsa UPDATE "String or binary data would
            //    be truncated" hatası verir ve MİGRATİON YARIDA KALIR.
            //    LEFT ile kırpma işini biz üstleniyoruz — hata yerine kısaltma.
            migrationBuilder.Sql(@"
        UPDATE oi
        SET oi.ProductName = LEFT(p.Name, 200)
        FROM OrderItems oi
        INNER JOIN Products p ON p.Id = oi.ProductId;
    ");

            // 2) Ürünü silinmiş kalemler için okunabilir bir metin bırak.
            //
            //    Yukarıdaki INNER JOIN, ürünü artık var olmayan kalemlerle
            //    EŞLEŞMEZ — o satırlarda ProductName boş string ('') kalır.
            //    Panelde boş bir hücre "bir şey bozuk" hissi verir; açık bir
            //    metin ise "ürün silinmiş, kayıt duruyor" bilgisini taşır.
            //
            //    N'...' önekindeki N nedir?
            //    Dizeyi Unicode (nvarchar) olarak işaretler. N olmadan SQL
            //    Server dizeyi varchar sanar ve Türkçe karakterler (ü, ş, ı)
            //    soru işaretine dönüşebilir.
            migrationBuilder.Sql(@"
        UPDATE OrderItems
        SET ProductName = N'(ürün silinmiş)'
        WHERE ProductName = N'';
    ");

            // 3) UnitCost'a BİLEREK DOKUNMUYORUZ.
            //
            //    Products.Cost'tan kopyalamak cazip görünüyor ama YANLIŞ olur:
            //    maliyet zamanla değişen bir değer. Bugünkü maliyeti geçen
            //    yılın siparişine yazmak, uydurulmuş bir kâr rakamı üretir ve
            //    rapor onu gerçek sanar.
            //
            //    null kalması dürüst davranıştır: rapor "bu dönem için maliyet
            //    bilinmiyor" diyebilir. Yanlış sayı, eksik sayıdan tehlikelidir.
            //
            //    IsHidden'a da dokunmuyoruz — varsayılan false zaten doğru.

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "ProductName",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "OrderItems");
        }
    }
}
