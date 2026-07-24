using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaretAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderNumberAndShippingSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderNumber",
                table: "Orders",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShippingCity",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShippingFullAddress",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShippingFullName",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShippingTitle",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");


            // ============================================================
            //  BACKFILL — mevcut siparişleri doldur
            //
            //  Bu iki UPDATE olmadan CreateIndex patlar: tüm eski satırlar
            //  boş ('') değere sahip olur ve unique index bunu kabul etmez.
            // ============================================================

            // 1) Eski siparişlere numara ver.
            //    Rastgele değil Id kullanıyoruz: Id zaten benzersiz olduğu için
            //    çakışma ihtimali sıfır. (Yeni siparişlerde rastgele üreteceğiz.)
            migrationBuilder.Sql(@"
        UPDATE Orders
        SET OrderNumber =
            'SP-' + FORMAT(CreatedAt, 'yyMMdd') + '-' +
            RIGHT('0000' + CAST(Id AS VARCHAR(10)), 4)
        WHERE OrderNumber IS NULL OR OrderNumber = '';
    ");

            // 2) Eski siparişlerin teslimat adresini dondur.
            //    Adres ve kullanıcı tablolarından O ANKİ hali kopyalanıyor.
            //    (Geçmişteki gerçek adres bu olmayabilir — o veri zaten kayıp.
            //     Bugünden itibaren doğru çalışacak, eskiler en iyi tahmin.)
            migrationBuilder.Sql(@"
        UPDATE o
        SET o.ShippingFullName    = u.FullName,
            o.ShippingTitle       = a.Title,
            o.ShippingCity        = a.City,
            o.ShippingFullAddress = a.FullAddress
        FROM Orders o
        INNER JOIN Addresses a ON a.Id = o.AddressId
        INNER JOIN Users     u ON u.Id = o.UserId;
    ");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderNumber",
                table: "Orders",
                column: "OrderNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_OrderNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OrderNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingCity",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingFullAddress",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingFullName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingTitle",
                table: "Orders");
        }
    }
}
