using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaretAPI.Migrations
{
    /// <inheritdoc />
    public partial class OdemeAltyapisi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IyzicoCardUserKey",
                table: "Users",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankaAdi",
                table: "Cards",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BinNumarasi",
                table: "Cards",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IyzicoCardToken",
                table: "Cards",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IyzicoBildirimleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IyziReferenceCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OlayTipi = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Token = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IyzicoPaymentId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Durum = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ImzaGecerliMi = table.Column<bool>(type: "bit", nullable: false),
                    HamGovde = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GelisZamani = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IslendiMi = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IyzicoBildirimleri", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OdemeIslemleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ConversationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Token = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TokenGecerlilik = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Durum = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IyzicoPaymentId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Taksit = table.Column<int>(type: "int", nullable: false),
                    ParaBirimi = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    FraudDurumu = table.Column<int>(type: "int", nullable: true),
                    MdStatus = table.Column<int>(type: "int", nullable: true),
                    HataKodu = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    HataMesaji = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    KartTipi = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    KartAilesi = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    BinNumarasi = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    Son4Hane = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    HamCevap = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OlusturmaZamani = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TamamlanmaZamani = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OdemeIslemleri", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OdemeKalemleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OdemeIslemiId = table.Column<int>(type: "int", nullable: false),
                    OrderItemId = table.Column<int>(type: "int", nullable: false),
                    IyzicoPaymentTransactionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IadeEdilenTutar = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OdemeKalemleri", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IyzicoBildirimleri_GelisZamani",
                table: "IyzicoBildirimleri",
                column: "GelisZamani");

            migrationBuilder.CreateIndex(
                name: "IX_IyzicoBildirimleri_IyziReferenceCode",
                table: "IyzicoBildirimleri",
                column: "IyziReferenceCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OdemeIslemleri_ConversationId",
                table: "OdemeIslemleri",
                column: "ConversationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OdemeIslemleri_Durum_OlusturmaZamani",
                table: "OdemeIslemleri",
                columns: new[] { "Durum", "OlusturmaZamani" });

            migrationBuilder.CreateIndex(
                name: "IX_OdemeIslemleri_OrderId_OlusturmaZamani",
                table: "OdemeIslemleri",
                columns: new[] { "OrderId", "OlusturmaZamani" });

            migrationBuilder.CreateIndex(
                name: "IX_OdemeIslemleri_Token",
                table: "OdemeIslemleri",
                column: "Token");

            migrationBuilder.CreateIndex(
                name: "IX_OdemeKalemleri_OdemeIslemiId",
                table: "OdemeKalemleri",
                column: "OdemeIslemiId");

            migrationBuilder.CreateIndex(
                name: "IX_OdemeKalemleri_OrderItemId",
                table: "OdemeKalemleri",
                column: "OrderItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IyzicoBildirimleri");

            migrationBuilder.DropTable(
                name: "OdemeIslemleri");

            migrationBuilder.DropTable(
                name: "OdemeKalemleri");

            migrationBuilder.DropColumn(
                name: "IyzicoCardUserKey",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BankaAdi",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "BinNumarasi",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "IyzicoCardToken",
                table: "Cards");
        }
    }
}
