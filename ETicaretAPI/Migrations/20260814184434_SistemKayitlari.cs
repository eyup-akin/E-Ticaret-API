using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaretAPI.Migrations
{
    /// <inheritdoc />
    public partial class SistemKayitlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IpAdresi",
                table: "AuditLogs",
                type: "nvarchar(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailKayitlari",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Alici = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Konu = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Olay = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Basarili = table.Column<bool>(type: "bit", nullable: false),
                    HataMesaji = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SaglayiciMesajId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    GovdeHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailKayitlari", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GirisKayitlari",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Sonuc = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IpAdresi = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GirisKayitlari", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HataKayitlari",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Yol = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Yontem = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Mesaj = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    YiginIzi = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    KullaniciId = table.Column<int>(type: "int", nullable: true),
                    IpAdresi = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HataKayitlari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailKayitlari_CreatedAt",
                table: "EmailKayitlari",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GirisKayitlari_CreatedAt",
                table: "GirisKayitlari",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_HataKayitlari_CreatedAt",
                table: "HataKayitlari",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailKayitlari");

            migrationBuilder.DropTable(
                name: "GirisKayitlari");

            migrationBuilder.DropTable(
                name: "HataKayitlari");

            migrationBuilder.DropColumn(
                name: "IpAdresi",
                table: "AuditLogs");
        }
    }
}
