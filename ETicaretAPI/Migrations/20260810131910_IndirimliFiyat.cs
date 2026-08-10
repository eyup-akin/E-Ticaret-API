using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaretAPI.Migrations
{
    /// <inheritdoc />
    public partial class IndirimliFiyat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EskiFiyat",
                table: "Products",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EskiFiyat",
                table: "OrderItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            // ⚠️⚠️ defaultValue ELLE true YAPILDI — EF false üretmişti.
            //
            // Modelde `= true` yazılı ama EF, C# property initializer'ını
            // migration'ın defaultValue'suna TAŞIMIYOR; her bool için
            // körü körüne false yazıyor.
            //
            // Fark ettirmeseydik mevcut BÜTÜN kuponlar false ile dolacak,
            // yani bir gecede "hiçbir kupon indirimli üründe geçmez"
            // hâline gelecekti. Kimse bir şey değiştirmemiş olurdu ama
            // müşteriler kuponlarının çalışmadığını görecekti.
            //
            // true olması UYDURMA DEĞİL: bugüne kadar "indirimli ürün"
            // diye bir kavram yoktu, mevcut kuponlar fiilen her ürüne
            // işliyordu. true, o davranışı aynen koruyor.
            //
            // (CLAUDE.md'deki "migration oluşunca dosyayı açıp içine bak"
            //  kuralı tam olarak bunun için var.)
            migrationBuilder.AddColumn<bool>(
                name: "IndirimliUrunlerdeGecerli",
                table: "Coupons",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EskiFiyat",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "EskiFiyat",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "IndirimliUrunlerdeGecerli",
                table: "Coupons");
        }
    }
}
